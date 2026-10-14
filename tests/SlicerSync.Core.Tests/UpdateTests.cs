using System.Net;
using System.Security.Cryptography;
using System.Text;
using SlicerSync.Core.Updates;

namespace SlicerSync.Core.Tests;

public sealed class UpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SlicerSyncUpdateTests", Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>Answers the releases API and the asset download.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;
        public FakeGitHub(Func<HttpRequestMessage, HttpResponseMessage> answer) => _answer = answer;
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(_answer(request));
        }
    }

    private static string ReleaseJson(string tag, string setupSha, string portableSha) => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/unwritten-dev/SlicerSync/releases/tag/{{tag}}",
          "body": "- Fixes",
          "assets": [
            { "name": "SlicerSync-1.0.8-portable.exe", "browser_download_url": "https://github.com/dl/portable.exe", "digest": "sha256:{{portableSha}}" },
            { "name": "SlicerSync-Setup-1.0.8.exe", "browser_download_url": "https://github.com/dl/setup.exe", "digest": "sha256:{{setupSha}}" },
            { "name": "notes.txt", "browser_download_url": "https://github.com/dl/notes.txt" }
          ]
        }
        """;

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    [Theory]
    [InlineData("v1.0.7", "1.0.7")]
    [InlineData("1.0.7+3708399abc", "1.0.7")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("v2.0.0-beta1", "2.0.0")]
    public void ParseVersion(string text, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(text));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData(null)]
    public void ParseVersion_Invalid(string? text) => Assert.Null(UpdateChecker.ParseVersion(text));

    [Fact]
    public async Task GetLatest_FindsVersionAndBothDownloads()
    {
        var fake = new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReleaseJson("v1.0.8", "aa", "bb"), Encoding.UTF8, "application/json"),
        });

        var info = await new UpdateChecker(new HttpClient(fake)).GetLatestAsync();

        Assert.Equal(new Version(1, 0, 8), info!.Version);
        Assert.Equal("https://api.github.com/repos/unwritten-dev/SlicerSync/releases/latest", fake.Requests.Single());
        Assert.Equal("https://github.com/dl/setup.exe", info.AssetFor(portable: false)!.Url);
        Assert.Equal("aa", info.Setup!.Sha256);
        Assert.Equal("https://github.com/dl/portable.exe", info.AssetFor(portable: true)!.Url);
    }

    [Fact]
    public async Task GetLatest_NoReleaseYet_ReturnsNull()
    {
        var fake = new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Null(await new UpdateChecker(new HttpClient(fake)).GetLatestAsync());
    }

    [Fact]
    public async Task Download_VerifiesChecksum()
    {
        var content = Encoding.UTF8.GetBytes("new exe");
        var fake = new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        var checker = new UpdateChecker(new HttpClient(fake));

        var file = await checker.DownloadAsync(new UpdateAsset("SlicerSync-Setup-1.0.8.exe", "https://github.com/dl/setup.exe", Sha(content)), _dir);

        Assert.Equal(content, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task Download_WrongChecksum_IsRejectedAndLeavesNothing()
    {
        var fake = new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("tampered")) });
        var checker = new UpdateChecker(new HttpClient(fake));

        await Assert.ThrowsAsync<UpdateException>(() =>
            checker.DownloadAsync(new UpdateAsset("SlicerSync-Setup-1.0.8.exe", "https://github.com/dl/setup.exe", Sha(Encoding.UTF8.GetBytes("original"))), _dir));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Download_WithoutChecksumOrHttps_IsRefused()
    {
        var fake = new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1]) });
        var checker = new UpdateChecker(new HttpClient(fake));

        await Assert.ThrowsAsync<UpdateException>(() => checker.DownloadAsync(new UpdateAsset("a.exe", "https://github.com/a.exe", null), _dir));
        await Assert.ThrowsAsync<UpdateException>(() => checker.DownloadAsync(new UpdateAsset("a.exe", "http://github.com/a.exe", "aa"), _dir));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public void ReplacePortableExe_SwapsFiles_AndCleanupRemovesOldOne()
    {
        Directory.CreateDirectory(_dir);
        var exe = Path.Combine(_dir, "SlicerSync.exe");
        var downloaded = Path.Combine(_dir, "SlicerSync-1.0.8-portable.exe");
        File.WriteAllText(exe, "old");
        File.WriteAllText(downloaded, "new");

        UpdateChecker.ReplacePortableExe(exe, downloaded);

        Assert.Equal("new", File.ReadAllText(exe));
        Assert.Equal("old", File.ReadAllText(exe + ".old"));
        UpdateChecker.CleanupOldExe(exe);
        Assert.False(File.Exists(exe + ".old"));
    }

    [Fact]
    public void ReplacePortableExe_WhileRunning_Works()
    {
        // Windows allows renaming an EXE that is in use; simulate with an open file handle.
        Directory.CreateDirectory(_dir);
        var exe = Path.Combine(_dir, "SlicerSync.exe");
        var downloaded = Path.Combine(_dir, "new.exe");
        File.WriteAllText(exe, "old");
        File.WriteAllText(downloaded, "new");

        using (new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            UpdateChecker.ReplacePortableExe(exe, downloaded);

        Assert.Equal("new", File.ReadAllText(exe));
    }
}
