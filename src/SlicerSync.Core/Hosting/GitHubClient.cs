using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SlicerSync.Core.Hosting;

public sealed record CreatedRepository(string FullName, string CloneUrl, string WebUrl);

/// <summary>Minimal GitHub REST client: creates a private repository in the signed-in user's account.</summary>
public sealed class GitHubClient
{
    public const string Host = "github.com";

    private readonly HttpClient _http;
    private readonly Uri _apiBase;

    public GitHubClient(HttpClient? http = null, string apiBase = "https://api.github.com/")
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _apiBase = new Uri(apiBase.EndsWith('/') ? apiBase : apiBase + "/");
    }

    public async Task<CreatedRepository> CreatePrivateRepositoryAsync(string token, string name, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_apiBase, "user/repos"))
        {
            Content = JsonContent.Create(new
            {
                name,
                @private = true,
                description = "Slicer profiles (backed up with SlicerSync)",
                auto_init = false,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.ParseAdd("SlicerSync");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new HostingException(Loc.T($"GitHub is not reachable: {ex.Message}", $"GitHub ist nicht erreichbar: {ex.Message}"), ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Created:
                    var repo = JsonSerializer.Deserialize<RepoResponse>(body)
                               ?? throw new HostingException(Loc.T("Unexpected response from GitHub.", "Unerwartete Antwort von GitHub."));
                    return new CreatedRepository(repo.FullName, repo.CloneUrl, repo.HtmlUrl);
                case HttpStatusCode.Unauthorized:
                    throw new GitHubAuthException(Loc.T(
                        "The GitHub sign-in is invalid or has expired. Please try again.",
                        "Die GitHub-Anmeldung ist ungültig oder abgelaufen. Bitte erneut versuchen."));
                case HttpStatusCode.Forbidden:
                case HttpStatusCode.NotFound:
                    throw new HostingException(Loc.T(
                        "The GitHub sign-in is not allowed to create repositories. Please create the repository on github.com " +
                        $"yourself and enter its URL. ({(int)response.StatusCode}: {ErrorMessage(body)})",
                        "Die GitHub-Anmeldung darf keine Repositories anlegen. Bitte das Repository auf github.com von Hand anlegen " +
                        $"und die URL eintragen. ({(int)response.StatusCode}: {ErrorMessage(body)})"));
                case HttpStatusCode.UnprocessableEntity:
                    throw new HostingException(Loc.T(
                        $"The repository \"{name}\" could not be created. A repository with this name probably exists already. ({ErrorMessage(body)})",
                        $"Das Repository „{name}“ konnte nicht angelegt werden. Vermutlich existiert bereits eins mit diesem Namen. ({ErrorMessage(body)})"));
                default:
                    throw new HostingException(Loc.T(
                        $"GitHub reported an error ({(int)response.StatusCode}): {ErrorMessage(body)}",
                        $"GitHub meldet einen Fehler ({(int)response.StatusCode}): {ErrorMessage(body)}"));
            }
        }
    }

    private static string ErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var details = errors.EnumerateArray()
                    .Select(e => e.TryGetProperty("message", out var em) ? em.GetString() : null)
                    .Where(s => !string.IsNullOrEmpty(s));
                message = string.Join("; ", new[] { message }.Concat(details).Where(s => !string.IsNullOrEmpty(s)));
            }
            return string.IsNullOrEmpty(message) ? body : message;
        }
        catch (JsonException)
        {
            return body.Length > 200 ? body[..200] : body;
        }
    }

    private sealed class RepoResponse
    {
        [JsonPropertyName("full_name")] public string FullName { get; set; } = "";
        [JsonPropertyName("clone_url")] public string CloneUrl { get; set; } = "";
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    }
}

public sealed class GitHubAuthException : Exception
{
    public GitHubAuthException(string message) : base(message) { }
}
