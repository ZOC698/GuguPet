using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace GuguPet;

public sealed record UpdateRelease(
    string TagName,
    Version Version,
    string AssetName,
    Uri DownloadUri,
    string ExpectedSha256);

public static class UpdateService
{
    private const string LatestReleaseEndpoint = "https://api.github.com/repos/ZOC698/GuguPet/releases/latest";
    private static readonly HttpClient Client = CreateClient();

    public static Version CurrentVersion
    {
        get
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion?
                .Split('+', 2)[0];
            return ParseVersion(informational) ??
                   Assembly.GetExecutingAssembly().GetName().Version ??
                   new Version(0, 0, 0);
        }
    }

    public static async Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = ParseVersion(tag);
        if (version is null || version <= CurrentVersion) return null;
        var expectedPackageName = $"GuguPet-Windows-x64-v{version.ToString(3)}.zip";

        ReleaseAsset? package = null;
        ReleaseAsset? checksums = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
            var digest = asset.TryGetProperty("digest", out var digestNode)
                ? digestNode.GetString()
                : null;
            var parsed = new ReleaseAsset(name, uri, digest);
            if (name.Equals(expectedPackageName, StringComparison.OrdinalIgnoreCase) &&
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                package = parsed;
            else if (name.StartsWith("SHA256SUMS", StringComparison.OrdinalIgnoreCase) &&
                     name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                checksums = parsed;
        }

        if (package is null)
            throw new InvalidDataException("The release does not contain a Windows x64 package.");

        var expectedHash = ParseDigest(package.Digest);
        if (expectedHash is null && checksums is not null)
        {
            var text = await Client.GetStringAsync(checksums.DownloadUri, cancellationToken);
            expectedHash = ParseChecksumFile(text, package.Name);
        }
        if (expectedHash is null)
            throw new InvalidDataException("The release does not provide a SHA-256 checksum.");

        return new UpdateRelease(tag, version, package.Name, package.DownloadUri, expectedHash);
    }

    public static async Task<string> DownloadAsync(UpdateRelease release, CancellationToken cancellationToken)
    {
        var safeTag = string.Concat(release.TagName.Where(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_'));
        if (string.IsNullOrWhiteSpace(safeTag)) safeTag = release.Version.ToString();
        var directory = Path.Combine(AppPaths.UpdatesDirectory, safeTag);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, release.AssetName);
        if (File.Exists(destination) && HashFile(destination).Equals(release.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            return destination;

        var partial = destination + ".partial";
        using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUri);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            await input.CopyToAsync(output, cancellationToken);

        var actualHash = HashFile(partial);
        if (!actualHash.Equals(release.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partial);
            throw new InvalidDataException("The downloaded update failed SHA-256 verification.");
        }
        File.Move(partial, destination, true);
        return destination;
    }

    public static bool TryLaunchInstaller(string packagePath, out string error)
    {
        try
        {
            var installedUpdater = Path.Combine(AppContext.BaseDirectory, "GuguPet.Updater.exe");
            if (!File.Exists(installedUpdater))
                throw new FileNotFoundException("GuguPet.Updater.exe is missing.", installedUpdater);
            var updateDirectory = Path.GetDirectoryName(packagePath)
                ?? throw new InvalidOperationException("The update directory is unavailable.");
            var runner = Path.Combine(updateDirectory, "GuguPet.Updater.Runner.exe");
            File.Copy(installedUpdater, runner, true);
            CodexLaunchWatcherManager.SignalWatcherToStop();

            var startInfo = new ProcessStartInfo(runner)
            {
                UseShellExecute = true,
                WorkingDirectory = updateDirectory
            };
            startInfo.ArgumentList.Add("--package");
            startInfo.ArgumentList.Add(Path.GetFullPath(packagePath));
            startInfo.ArgumentList.Add("--target");
            startInfo.ArgumentList.Add(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            startInfo.ArgumentList.Add("--pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(startInfo);
            error = "";
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GuguPet", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static Version? ParseVersion(string? value) =>
        Version.TryParse(value?.Trim().TrimStart('v', 'V'), out var version) ? version : null;

    private static string? ParseDigest(string? digest)
    {
        if (digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
        var hash = digest[7..].Trim();
        return hash.Length == 64 && hash.All(Uri.IsHexDigit) ? hash.ToUpperInvariant() : null;
    }

    private static string? ParseChecksumFile(string text, string assetName)
    {
        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit) &&
                parts[^1].TrimStart('*').Equals(assetName, StringComparison.OrdinalIgnoreCase))
                return parts[0].ToUpperInvariant();
        }
        return null;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record ReleaseAsset(string Name, Uri DownloadUri, string? Digest);
}
