using System.Net;
using System.Text;
using SlicerSync.Core.FileSystem;
using SlicerSync.Core.Hosting;
using SlicerSync.Core.Logging;
using SlicerSync.Core.Setup;

namespace SlicerSync.Core.Tests;

public sealed class HostingTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    /// <summary>Simulates the GitHub API and remembers the last request.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public FakeGitHub(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content != null ? await request.Content.ReadAsStringAsync(ct) : null;
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }

    private const string CreatedJson =
        "{\"full_name\":\"max/slicer-profiles\",\"clone_url\":\"https://github.com/max/slicer-profiles.git\",\"html_url\":\"https://github.com/max/slicer-profiles\"}";

    /// <summary>Git runner whose credential helper returns a fixed token and logs get/store/erase calls.</summary>
    private Core.Git.GitRunner RunnerWithCredentialHelper(out string protocolFile)
    {
        protocolFile = Path.Combine(_env.Root, "credential-calls.txt").Replace('\\', '/');
        var config = Path.Combine(_env.Root, "gitconfig-cred");
        File.WriteAllText(config,
            "[credential]\n" +
            $"\thelper = \"!f() {{ echo \\\"$1\\\" >> '{protocolFile}'; if [ \\\"$1\\\" = get ]; then echo username=max; echo password=gho_test; fi; }}; f\"\n");
        var runner = _env.CreateRunner();
        runner.Environment["GIT_CONFIG_GLOBAL"] = config;
        return runner;
    }

    [Fact]
    public void Credential_Fill_ReturnsHelperValues()
    {
        var git = RunnerWithCredentialHelper(out _);
        var credential = GitCredential.Fill(git, "github.com");
        Assert.Equal("max", credential.Username);
        Assert.Equal("gho_test", credential.Password);
    }

    [Fact]
    public void Credential_Fill_WithoutHelper_FailsWithHint()
    {
        var ex = Assert.Throws<HostingException>(() => GitCredential.Fill(_env.CreateRunner(), "github.com"));
        Assert.Contains("Credential Manager", ex.Message);
    }

    [Fact]
    public async Task GitHub_CreatesPrivateRepository_AndApprovesCredential()
    {
        var git = RunnerWithCredentialHelper(out var protocol);
        var fake = new FakeGitHub(HttpStatusCode.Created, CreatedJson);
        var creator = new RemoteCreator(git, new MemoryLogger(), new GitHubClient(new HttpClient(fake)));

        var repo = await creator.CreateOnGitHubAsync("slicer-profiles");

        Assert.Equal("https://github.com/max/slicer-profiles.git", repo.CloneUrl);
        Assert.Equal("https://api.github.com/user/repos", fake.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer gho_test", fake.Request.Headers.Authorization!.ToString());
        Assert.Contains("\"private\":true", fake.RequestBody);
        Assert.Contains("\"name\":\"slicer-profiles\"", fake.RequestBody);
        Assert.Contains("store", File.ReadAllText(protocol));
    }

    [Fact]
    public async Task GitHub_Unauthorized_RejectsCredential()
    {
        var git = RunnerWithCredentialHelper(out var protocol);
        var fake = new FakeGitHub(HttpStatusCode.Unauthorized, "{\"message\":\"Bad credentials\"}");
        var creator = new RemoteCreator(git, new MemoryLogger(), new GitHubClient(new HttpClient(fake)));

        await Assert.ThrowsAsync<GitHubAuthException>(() => creator.CreateOnGitHubAsync("slicer-profiles"));
        Assert.Contains("erase", File.ReadAllText(protocol));
    }

    [Fact]
    public async Task GitHub_NameExists_GivesClearMessage()
    {
        var git = RunnerWithCredentialHelper(out _);
        var fake = new FakeGitHub(HttpStatusCode.UnprocessableEntity,
            "{\"message\":\"Repository creation failed.\",\"errors\":[{\"message\":\"name already exists on this account\"}]}");
        var creator = new RemoteCreator(git, new MemoryLogger(), new GitHubClient(new HttpClient(fake)));

        var ex = await Assert.ThrowsAsync<HostingException>(() => creator.CreateOnGitHubAsync("slicer-profiles"));
        Assert.Contains("probably exists already", ex.Message);
        Assert.Contains("name already exists", ex.Message);
    }

    [Fact]
    public async Task GitHub_InvalidName_FailsBeforeAnyRequest()
    {
        var fake = new FakeGitHub(HttpStatusCode.Created, CreatedJson);
        var creator = new RemoteCreator(_env.CreateRunner(), new MemoryLogger(), new GitHubClient(new HttpClient(fake)));

        await Assert.ThrowsAsync<HostingException>(() => creator.CreateOnGitHubAsync("my profiles"));
        Assert.Null(fake.Request);
    }

    [Theory]
    [InlineData("slicer-profiles", true)]
    [InlineData("Slicer_Profiles.v2", true)]
    [InlineData("", false)]
    [InlineData("with space", false)]
    [InlineData("profile-ä", false)]
    [InlineData("profile.git", false)]
    [InlineData("..", false)]
    public void ValidateName(string name, bool valid) =>
        Assert.Equal(valid, RemoteCreator.ValidateName(name) == null);

    [Fact]
    public void GitLabUrl_IsBuiltFromParts()
    {
        Assert.Equal("https://gitlab.com/max/slicer-profiles.git", RemoteCreator.GitLabUrl("", "max", "slicer-profiles"));
        Assert.Equal("https://git.example.com/team/3d/profiles.git", RemoteCreator.GitLabUrl("git.example.com", "team/3d/", "profiles"));
        Assert.Throws<HostingException>(() => RemoteCreator.GitLabUrl("gitlab.com", "", "x"));
        Assert.Throws<HostingException>(() => RemoteCreator.GitLabUrl("gitlab.com/evil", "max", "x"));
    }

    [Fact]
    public void NewSetup_RemoteNotYetExisting_FailsNormally_ButWorksWithCreateOnPush()
    {
        var pc = _env.CreatePc("A");
        pc.CreateSampleProfiles();
        pc.Settings.RemoteUrl = Path.Combine(_env.Root, "does-not-exist-yet.git");

        Assert.Throws<SetupException>(() => pc.NewSetup());
        Assert.False(Junction.IsJunction(pc.PrusaDir("print")));

        var candidates = pc.Detector.Detect().Candidates;
        var result = pc.Setup.RunNewSetup(pc.Settings, candidates, createRemoteOnPush: true);

        Assert.True(pc.Settings.SetupCompleted);
        Assert.True(Junction.IsJunction(pc.PrusaDir("print")));
        // A local path is not created by a push → warning instead of failure.
        Assert.False(result.Pushed);
        Assert.Single(result.Warnings);
    }
}
