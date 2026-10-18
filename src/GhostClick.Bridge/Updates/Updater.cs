using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GhostClick.Bridge.Updates;

/// <summary>A newer release than the running one, and where its installer is.</summary>
/// <param name="Version">The release's version, <c>X.Y.Z</c>.</param>
/// <param name="SetupUrl">The installer, <c>GhostClick-Setup-X.Y.Z.exe</c>.</param>
/// <param name="SetupSize">The installer's size in bytes.</param>
/// <param name="ChecksumUrl">Its SHA-256, <c>GhostClick-Setup-X.Y.Z.exe.sha256</c>, published next to it.</param>
public sealed record UpdateInfo(string Version, Uri SetupUrl, long SetupSize, Uri ChecksumUrl);

/// <summary>Thrown when a downloaded installer is not the file the release published.</summary>
public sealed class UpdateChecksumException : Exception
{
    public UpdateChecksumException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Looks for a newer GhostClick on the project's GitHub Releases and downloads its installer.
/// <para>
/// Only a release whose installer has a published SHA-256 next to it is offered (CI publishes
/// both), and only files from github.com over HTTPS are fetched. The download is checked
/// against that SHA-256 before it is handed out; the app then runs it like the user would,
/// and the installer upgrades the installed copy in place (same AppId), keeping the profiles.
/// </para>
/// </summary>
public sealed partial class Updater
{
    /// <summary>The latest published release (drafts and pre-releases are not "latest").</summary>
    public static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/Sergey5600/GhostClick/releases/latest");

    private readonly HttpClient _http;
    private readonly string _downloadFolder;

    /// <param name="currentVersion">The running build's version; anything after <c>+</c> is ignored.</param>
    /// <param name="http">Used for every request; tests pass one with a fake handler.</param>
    /// <param name="downloadFolder">Where installers are saved.</param>
    public Updater(string currentVersion, HttpClient http, string downloadFolder)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        _http = http ?? throw new ArgumentNullException(nameof(http));
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadFolder);
        _downloadFolder = downloadFolder;

        CurrentVersion = currentVersion.Split('+')[0].Trim();
    }

    /// <summary>The running version, without build metadata: <c>1.2.0</c> or <c>1.2.0-dev.41</c>.</summary>
    public string CurrentVersion { get; }

    /// <summary>
    /// False for a test build (<c>1.2.0-dev.41</c>, a local <c>0.0.0</c>): those do not come
    /// from Releases, and offering the last release over them would replace what is being tested.
    /// </summary>
    public bool ChecksAutomatically => TryParse(CurrentVersion, out var version) && !CurrentVersion.Contains('-') && version > new Version(0, 0, 0);

    /// <summary>A short User-Agent, which GitHub's API requires.</summary>
    public static ProductInfoHeaderValue UserAgent { get; } = new("GhostClick", "1");

    /// <summary>
    /// The latest release when it is newer than <see cref="CurrentVersion"/> and has an
    /// installer with a checksum; null otherwise. Throws <see cref="HttpRequestException"/>
    /// or <see cref="JsonException"/> when GitHub cannot be reached or answers nonsense.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
        request.Headers.UserAgent.Add(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // No release at all yet.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Parse(document.RootElement, CurrentVersion);
    }

    /// <summary>
    /// Picks the update out of a release as the GitHub API describes it; null when it is not
    /// newer than <paramref name="currentVersion"/> or lacks the installer or its checksum.
    /// </summary>
    public static UpdateInfo? Parse(JsonElement release, string currentVersion)
    {
        if (release.ValueKind != JsonValueKind.Object ||
            !release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var name = tag.GetString()!.Trim();
        var versionText = name.StartsWith('v') || name.StartsWith('V') ? name[1..] : name;
        if (!TryParse(versionText, out var version) || versionText.Contains('-') ||
            !TryParse(currentVersion.Split('+')[0], out var current) || version <= current)
        {
            return null;
        }

        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var setupName = $"GhostClick-Setup-{versionText}.exe";
        (Uri Url, long Size)? setup = null;
        Uri? checksum = null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var assetName) || assetName.ValueKind != JsonValueKind.String ||
                !asset.TryGetProperty("browser_download_url", out var url) || url.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) || !IsTrusted(uri))
            {
                continue;
            }

            if (string.Equals(assetName.GetString(), setupName, StringComparison.OrdinalIgnoreCase))
            {
                long size = asset.TryGetProperty("size", out var bytes) && bytes.TryGetInt64(out var n) ? n : 0;
                setup = (uri, size);
            }
            else if (string.Equals(assetName.GetString(), setupName + ".sha256", StringComparison.OrdinalIgnoreCase))
            {
                checksum = uri;
            }
        }

        return setup is { } found && checksum is not null
            ? new UpdateInfo(versionText, found.Url, found.Size, checksum)
            : null;
    }

    /// <summary>
    /// Downloads the installer into the download folder, reporting the bytes received so far,
    /// and checks it against the published SHA-256. Returns its path. A cancelled or failed
    /// download leaves nothing behind.
    /// </summary>
    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var expected = await ReadChecksumAsync(update.ChecksumUrl, cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(_downloadFolder);
        var path = Path.Combine(_downloadFolder, $"GhostClick-Setup-{update.Version}.exe");
        var partial = path + ".part";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.SetupUrl);
            request.Headers.UserAgent.Add(UserAgent);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    received += read;
                    progress?.Report(received);
                }
            }

            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateChecksumException($"SHA-256 {actual} does not match the published {expected}.");
            }

            File.Move(partial, path, overwrite: true);
            return path;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    /// <summary>Removes installers left over from earlier updates.</summary>
    public void CleanUp()
    {
        try
        {
            if (Directory.Exists(_downloadFolder))
            {
                foreach (var file in Directory.EnumerateFiles(_downloadFolder, "GhostClick-Setup-*"))
                {
                    TryDelete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Whatever is left is harmless and goes next time.
        }
    }

    /// <summary>Only files the project publishes on GitHub, and only over HTTPS.</summary>
    public static bool IsTrusted(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith("/Sergey5600/GhostClick/releases/download/", StringComparison.OrdinalIgnoreCase);

    private async Task<string> ReadChecksumAsync(Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(UserAgent);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // "<hex>" or "<hex>  GhostClick-Setup-X.Y.Z.exe", as sha256sum writes it.
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var match = Sha256Pattern().Match(text);
        return match.Success ? match.Value : throw new UpdateChecksumException("The published checksum is not a SHA-256.");
    }

    private static bool TryParse(string text, out Version version)
    {
        version = new Version(0, 0, 0);
        var core = text.Split('-')[0].Trim();
        var parts = core.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use or gone; nothing more to do.
        }
    }

    [GeneratedRegex("\\b[0-9a-fA-F]{64}\\b")]
    private static partial Regex Sha256Pattern();
}

/// <summary>What the app does with a downloaded installer: run it when GhostClick closes.</summary>
public interface IUpdateHost
{
    /// <summary>
    /// Runs <paramref name="setupPath"/> once GhostClick has closed; with
    /// <paramref name="relaunch"/> the installer starts GhostClick again when it is done.
    /// </summary>
    void InstallOnExit(string setupPath, bool relaunch);

    /// <summary>Closes GhostClick, the way closing its window does.</summary>
    void Exit();
}
