using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace SnippingTool.Core;

/// <summary>
/// Update checking + downloading against the GitHub releases of the Shutio repo.
/// The repo is public (a token is still accepted, for a private mirror, from
/// Settings or the SHUTIO_GITHUB_TOKEN environment variable). The "update" itself
/// is performed by the release's installable asset: current releases ship
/// Shutio-setup.exe (the offline installer, run in place over the existing
/// install — Data\ is not part of the payload and is kept); older releases also
/// had Shutio-Updater.exe, which is preferred when present.
/// Everything here is best-effort and must never take the app down.
/// </summary>
public static class UpdateService
{
    public enum UpdateAssetKind { Setup, Updater }

    public sealed record ReleaseInfo(string Version, string Name, string? AssetUrl, string Body)
    {
        public UpdateAssetKind AssetKind { get; init; } = UpdateAssetKind.Setup;
        public bool HasAsset => !string.IsNullOrWhiteSpace(AssetUrl);
    }

    public sealed record FetchResult(ReleaseInfo? Info, string? Error);

    public static string CurrentVersion =>
        typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private const string ApiBase = "https://api.github.com";
    private const string Repo = "EvilLighty/Shutio";
    private const string UpdaterAssetName = "Shutio-Updater.exe";
    private const string SetupAssetName   = "Shutio-setup.exe";

    public static string? ResolveToken(string? fromSettings)
    {
        string? token = string.IsNullOrWhiteSpace(fromSettings)
            ? Environment.GetEnvironmentVariable("SHUTIO_GITHUB_TOKEN")
            : fromSettings;
        return token?.Trim();
    }

    public static bool IsNewer(string candidate, string current)
    {
        if (TryParseVersion(candidate, out var a) && TryParseVersion(current, out var b))
            return a > b;
        return !string.Equals(candidate.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<FetchResult> FetchLatestReleaseAsync(string? token, int timeoutMs = 15000)
    {
        using var http = CreateHttp(token, timeoutMs);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/repos/{Repo}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("Shutio-UpdateCheck/1.0");

        using var response = await http.SendAsync(request).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new FetchResult(null, string.IsNullOrWhiteSpace(token)
                ? $"GitHub reports no latest release for {Repo} (404). If the repo is private, add a token in Settings → Update; otherwise check the connection."
                : $"GitHub returned 404 for the latest release of {Repo}.");
        if (!response.IsSuccessStatusCode)
            return new FetchResult(null, $"GitHub returned {(int)response.StatusCode} ({response.StatusCode}) while checking for updates.");

        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string tag = GetString(root, "tag_name") ?? "";
        string name = GetString(root, "name") ?? tag;
        string body = GetString(root, "body") ?? "";
        if (tag.Length == 0)
            return new FetchResult(null, "The release response had no tag name.");

        // Releases ship the offline installer (Shutio-setup.exe); older releases also had
        // Shutio-Updater.exe. Prefer the updater when present, else the installer.
        string? assetUrl = null, setupUrl = null;
        UpdateAssetKind kind = UpdateAssetKind.Setup;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string assetName = GetString(asset, "name") ?? "";
                if (string.Equals(assetName, UpdaterAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    assetUrl = GetString(asset, "browser_download_url");
                    kind = UpdateAssetKind.Updater;
                    break;
                }
                if (string.Equals(assetName, SetupAssetName, StringComparison.OrdinalIgnoreCase))
                    setupUrl = GetString(asset, "browser_download_url");
            }
        }
        assetUrl ??= setupUrl;

        string version = tag.Trim().TrimStart('v', 'V');
        if (!TryParseVersion(version, out _))
            version = tag;
        return new FetchResult(new ReleaseInfo(version, name, assetUrl, body) { AssetKind = kind }, null);
    }

    public static async Task DownloadFileAsync(string url, string? token, string destPath,
        Action<string>? status, CancellationToken ct = default)
    {
        using var http = CreateHttp(token, 5 * 60 * 1000);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Shutio-UpdateCheck/1.0");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub returned {(int)response.StatusCode} while downloading the update.");

        long? total = response.Content.Headers.ContentLength;
        using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        string tmp = destPath + ".part";
        await using var dest = File.Create(tmp);
        var buffer = new byte[81920];
        long read = 0;
        var lastStatus = DateTime.MinValue;
        int n;
        while ((n = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;
            if (total is > 0 && (DateTime.UtcNow - lastStatus).TotalMilliseconds >= 250)
            {
                lastStatus = DateTime.UtcNow;
                status?.Invoke($"Downloading the update… {read * 100.0 / total.Value:0}% ({read / 1048576:N1} MB)");
            }
        }
        File.Move(tmp, destPath, true);
        status?.Invoke("Download complete.");
    }

    private static HttpClient CreateHttp(string? token, int timeoutMs)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
        if (!string.IsNullOrWhiteSpace(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        return http;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryParseVersion(string text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim().TrimStart('v', 'V');
        s = s.Split('-')[0].Split('+')[0];
        if (!s.Contains('.')) s += ".0";
        return Version.TryParse(s, out version);
    }
}
