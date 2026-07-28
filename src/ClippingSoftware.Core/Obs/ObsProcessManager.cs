using System.Diagnostics;

namespace ClippingSoftware.Core.Obs;

public class ObsProcessManager
{
    public bool IsObsRunning() => Process.GetProcessesByName("obs64").Length > 0;

    public void EnsureRunning(string obsExecutablePath)
    {
        if (IsObsRunning())
        {
            return;
        }

        if (!File.Exists(obsExecutablePath))
        {
            throw new FileNotFoundException("OBS Studio executable not found. Check Settings > OBS Executable Path.", obsExecutablePath);
        }

        var workingDirectory = Path.GetDirectoryName(obsExecutablePath)!;
        var startInfo = new ProcessStartInfo
        {
            FileName = obsExecutablePath,
            WorkingDirectory = workingDirectory,
            Arguments = "--minimize-to-tray --profile Untitled --collection Untitled",
            UseShellExecute = true,
        };
        using var process = Process.Start(startInfo);
    }
}
