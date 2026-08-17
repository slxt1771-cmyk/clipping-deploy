using System.Net.Http;
using System.Text.Json;

namespace ClippingSoftware.Core.Update;

/// <summary>One update found via CheckForUpdateAsync - everything DownloadUpdateAsync needs to fetch it.</summary>
public record AvailableUpdate(Version Version, string AssetDownloadUrl, string AssetFileName);

/// <summary>
/// Checks this repo's GitHub Releases for a newer published version than the one currently running, and
/// can download+launch the installer it finds. The repo is public, so this is all anonymous HTTPS - no
/// embedded credentials, same as any browser hitting the same URLs. GitHub's unauthenticated rate limit
/// (60 requests/hour/IP) is not a concern here since this checks once per app launch.
///
/// This intentionally doesn't replace the app's files itself - see installer/ClippingSoftware.iss and
/// RunSilentInstall's doc comment for why re-running the same Setup.exe silently is the update mechanism,
/// not a bespoke in-place file copy.
/// </summary>
public class UpdateChecker(string owner, string repo, HttpClient? httpClient = null)
{
    private readonly HttpClient _http = httpClient ?? new HttpClient();

    /// <summary>Null if there's no newer release, or the check itself failed for any reason (network,
    /// malformed response, no exe asset attached) - callers should treat all of those the same way: no
    /// update to offer right now, try again next launch.</summary>
    public async Task<AvailableUpdate?> CheckForUpdateAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{owner}/{repo}/releases/latest");
            // GitHub's REST API rejects requests with no User-Agent at all.
            request.Headers.UserAgent.ParseAdd("ClippingSoftware-UpdateChecker");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            var tagName = root.GetProperty("tag_name").GetString();
            if (tagName is null || !Version.TryParse(tagName.TrimStart('v'), out var remoteVersion))
            {
                return null;
            }

            if (remoteVersion <= currentVersion)
            {
                return null;
            }

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var downloadUrl = asset.GetProperty("browser_download_url").GetString();
                if (downloadUrl is null)
                {
                    continue;
                }

                return new AvailableUpdate(remoteVersion, downloadUrl, name);
            }

            // A release with no .exe asset attached (e.g. one still mid-upload, or a manually-drafted
            // release with just notes) - nothing to install yet.
            return null;
        }
        catch
        {
            // Best-effort, same reasoning as every other background/detection path in this codebase - a
            // failed update check should never be able to break the app it's checking updates for.
            return null;
        }
    }

    /// <summary>Downloads the installer to a temp file and reports 0-100 progress as it goes (via
    /// IProgress&lt;T&gt; so the caller gets it back on whatever SynchronizationContext it was constructed
    /// on - WPF's dispatcher context, if constructed on the UI thread, same as everywhere else in this app
    /// that reports async progress to bound UI state).</summary>
    public async Task<string> DownloadUpdateAsync(AvailableUpdate update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, update.AssetDownloadUrl);
        request.Headers.UserAgent.ParseAdd("ClippingSoftware-UpdateChecker");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var tempPath = Path.Combine(Path.GetTempPath(), update.AssetFileName);
        var totalBytes = response.Content.Headers.ContentLength;

        await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(tempPath);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;
        while ((read = await httpStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalRead += read;
            if (totalBytes is > 0)
            {
                progress?.Report((int)(totalRead * 100 / totalBytes.Value));
            }
        }

        return tempPath;
    }

    /// <summary>
    /// Launches the downloaded Setup.exe unattended - the update mechanism is "re-run the same installer
    /// a normal first-time install would use," not a bespoke in-place file replace, since the installer
    /// already handles everything correctly (fixed AppId upgrades the existing install in place; see
    /// installer/ClippingSoftware.iss). UseShellExecute=true is required, not incidental: the installer's
    /// manifest requests elevation (PrivilegesRequired=admin), and only ShellExecute lets Windows show the
    /// UAC prompt for that - a directly-launched (UseShellExecute=false) process fails to elevate. The
    /// installer's own CloseApplications setting closes this app's running process automatically once the
    /// file copy needs to start, and its [Run] entry (with skipifsilent removed) relaunches it afterward -
    /// this method does not need to (and should not try to) coordinate that handoff itself.
    /// </summary>
    public static void RunSilentInstall(string installerPath)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
            UseShellExecute = true,
        });
    }
}
