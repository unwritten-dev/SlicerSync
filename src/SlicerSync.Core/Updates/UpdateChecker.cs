using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SlicerSync.Core.Updates;

public sealed record UpdateAsset(string Name, string Url, string? Sha256);

public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string Notes, UpdateAsset? Setup, UpdateAsset? Portable)
{
    public UpdateAsset? AssetFor(bool portable) => portable ? Portable : Setup;
}

public sealed class UpdateException : Exception
{
    public UpdateException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Looks for a newer release on GitHub and downloads it. Only the public releases API is used;
/// nothing is sent except the request itself. Downloads are verified with the SHA-256 digest that
/// GitHub publishes for every release asset.
/// </summary>
public sealed class UpdateChecker
{
    public const string Repository = "unwritten-dev/SlicerSync";

    private static readonly Regex SetupPattern = new(@"^SlicerSync-Setup-[\d.]+\.exe$", RegexOptions.IgnoreCase);
    private static readonly Regex PortablePattern = new(@"^SlicerSync-[\d.]+-portable\.exe$", RegexOptions.IgnoreCase);

    private readonly HttpClient _http;
    private readonly Uri _apiBase;

    public UpdateChecker(HttpClient? http = null, string apiBase = "https://api.github.com/")
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _apiBase = new Uri(apiBase.EndsWith('/') ? apiBase : apiBase + "/");
    }

    /// <summary>"v1.0.7", "1.0.7+abc123" or "1.0.7-beta" → 1.0.7; null if it is no version.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = Regex.Match(text.Trim(), @"^v?(\d+)\.(\d+)(?:\.(\d+))?");
        if (!match.Success)
            return null;
        return new Version(
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0);
    }

    /// <summary>The latest published release (GitHub excludes drafts and pre-releases), or null if there is none.</summary>
    public async Task<UpdateInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_apiBase, $"repos/{Repository}/releases/latest"));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SlicerSync", "1"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException(Loc.T($"GitHub is not reachable: {ex.Message}", $"GitHub ist nicht erreichbar: {ex.Message}"), ex);
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
                throw new UpdateException(Loc.T(
                    $"Update check failed ({(int)response.StatusCode}).",
                    $"Update-Prüfung fehlgeschlagen ({(int)response.StatusCode})."));
            return Parse(await response.Content.ReadAsStringAsync(ct));
        }
    }

    internal static UpdateInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = ParseVersion(tag);
        if (version == null)
            return null;

        UpdateAsset? setup = null, portable = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString() ?? "";
                var digest = a.TryGetProperty("digest", out var d) ? d.GetString() : null;
                var sha = digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
                if (SetupPattern.IsMatch(name))
                    setup = new UpdateAsset(name, url, sha);
                else if (PortablePattern.IsMatch(name))
                    portable = new UpdateAsset(name, url, sha);
            }
        }

        return new UpdateInfo(
            version,
            tag,
            root.TryGetProperty("html_url", out var page) ? page.GetString() ?? "" : "",
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            setup,
            portable);
    }

    /// <summary>Downloads an asset into <paramref name="targetDir"/> and checks its SHA-256. Returns the file path.</summary>
    public async Task<string> DownloadAsync(UpdateAsset asset, string targetDir, CancellationToken ct = default)
    {
        if (!asset.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new UpdateException(Loc.T("Refusing to download over an insecure connection.", "Download über eine unsichere Verbindung abgelehnt."));
        if (string.IsNullOrEmpty(asset.Sha256))
            throw new UpdateException(Loc.T(
                $"No checksum is published for {asset.Name}; the update was not installed.",
                $"Für {asset.Name} ist keine Prüfsumme veröffentlicht; das Update wurde nicht installiert."));

        Directory.CreateDirectory(targetDir);
        var target = Path.Combine(targetDir, asset.Name);
        var partial = target + ".download";
        try
        {
            using (var response = await _http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(partial);
                await response.Content.CopyToAsync(file, ct);
            }

            string actual;
            await using (var file = File.OpenRead(partial))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
            if (!actual.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException(Loc.T(
                    $"The download of {asset.Name} is damaged (checksum mismatch); the update was not installed.",
                    $"Der Download von {asset.Name} ist beschädigt (Prüfsumme stimmt nicht); das Update wurde nicht installiert."));

            File.Move(partial, target, overwrite: true);
            return target;
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException(Loc.T($"Download failed: {ex.Message}", $"Download fehlgeschlagen: {ex.Message}"), ex);
        }
        finally
        {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }

    /// <summary>
    /// Portable version: swaps the running EXE for the downloaded one. Windows allows renaming a running
    /// EXE, so the old one is moved aside (deleted on the next start) and the new one takes its place.
    /// </summary>
    public static void ReplacePortableExe(string exePath, string newExePath)
    {
        var old = exePath + ".old";
        if (File.Exists(old))
            File.Delete(old);
        File.Move(exePath, old);
        try
        {
            File.Move(newExePath, exePath);
        }
        catch
        {
            File.Move(old, exePath); // roll back
            throw;
        }
    }

    /// <summary>Removes the EXE left over by a previous portable update.</summary>
    public static void CleanupOldExe(string exePath)
    {
        try
        {
            if (File.Exists(exePath + ".old"))
                File.Delete(exePath + ".old");
        }
        catch (IOException)
        {
            // Still in use shortly after the restart; it will be removed next time.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
