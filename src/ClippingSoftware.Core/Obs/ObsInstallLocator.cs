namespace ClippingSoftware.Core.Obs;

/// <summary>
/// Finds an installed OBS Studio without the user having to type a path. Used by the first-run setup
/// wizard to pre-fill (and validate) the OBS executable path.
///
/// Deliberately probes known install locations rather than reading the uninstall registry: the registry
/// route needs the path to have been recorded by whichever installer variant was used (the portable ZIP
/// build records nothing at all), while the folder layout under an OBS install root is fixed across every
/// distribution form. Anything this misses is still reachable through the wizard's Browse button, which
/// is why "probably finds it" is good enough here and a wrong guess is never silently accepted -
/// <see cref="Locate"/> only ever returns a path that exists.
/// </summary>
public static class ObsInstallLocator
{
    /// <summary>Path segments below an OBS install root that lead to the executable.</summary>
    private static readonly string[] ExecutableSubPath = ["bin", "64bit", "obs64.exe"];

    /// <summary>
    /// Returns the first obs64.exe that actually exists among the standard install locations, or null if
    /// none do. Ordered most- to least-likely: the 64-bit Program Files install is what the official
    /// installer produces on a normal machine.
    /// </summary>
    public static string? Locate()
    {
        foreach (var root in CandidateInstallRoots())
        {
            var candidate = Path.Combine(root, Path.Combine(ExecutableSubPath));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a path looks like a usable OBS executable. Used by the wizard to mark the field valid;
    /// checking the file exists is the whole test, since a user who browsed to something else entirely
    /// will find out immediately when the connection step fails.
    /// </summary>
    public static bool IsValidExecutablePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static IEnumerable<string> CandidateInstallRoots()
    {
        foreach (var programFiles in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (!string.IsNullOrEmpty(programFiles))
            {
                yield return Path.Combine(programFiles, "obs-studio");
            }
        }

        // A portable/manual install commonly ends up at the root of a secondary drive. Enumerating fixed
        // drives is cheap and catches the "I keep everything on D:" case that Program Files never will.
        foreach (var drive in GetFixedDriveRoots())
        {
            yield return Path.Combine(drive, "obs-studio");
            yield return Path.Combine(drive, "Program Files", "obs-studio");
        }
    }

    private static IEnumerable<string> GetFixedDriveRoots()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var drive in drives)
        {
            // IsReady guards against a mapped-but-disconnected drive, where RootDirectory access blocks
            // or throws rather than simply reporting "no".
            if (drive.DriveType == DriveType.Fixed && drive.IsReady)
            {
                yield return drive.RootDirectory.FullName;
            }
        }
    }
}
