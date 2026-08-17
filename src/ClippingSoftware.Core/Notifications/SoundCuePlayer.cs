using System.Windows.Media;

namespace ClippingSoftware.Core.Notifications;

/// <summary>
/// Plays the two short "Medal-style" UI sound cues (M13): one for a clip being saved (replay buffer save
/// or manual recording stop), one for a recording starting. Built on WPF's MediaPlayer rather than pulling
/// in a new audio library - Core already sets UseWPF (for GlobalHotkeyService's HwndSource interop), and
/// MediaPlayer's Volume property (0.0-1.0) gives the volume-adjuster requirement for free, unlike
/// System.Media.SoundPlayer/SystemSounds which have no per-instance volume control.
///
/// The bundled WAV files (assets/Sounds/clip-saved.wav, recording-started.wav) are synthesized tones -
/// short original sine-wave chimes generated once and checked in, not pulled from any third-party/
/// copyrighted source (see PROJECT.md). Replace either file in place (same name/location) to swap in your
/// own sound; nothing in code needs to change to pick up a replacement.
/// </summary>
public class SoundCuePlayer : IDisposable
{
    private readonly MediaPlayer _clipSavedPlayer = new();
    private readonly MediaPlayer _recordingStartedPlayer = new();
    private bool _disposed;

    public SoundCuePlayer(string? clipSavedSoundPath = null, string? recordingStartedSoundPath = null)
    {
        TryOpen(_clipSavedPlayer, clipSavedSoundPath ?? LocateSoundFile("clip-saved.wav"));
        TryOpen(_recordingStartedPlayer, recordingStartedSoundPath ?? LocateSoundFile("recording-started.wav"));
    }

    /// <summary>
    /// A cue whose WAV is missing or unreadable leaves its player empty, so Play() is a silent no-op
    /// instead of an exception. This runs from a MainViewModel field initializer, and a missing optional
    /// sound effect is never a good reason to stop the app from starting.
    /// </summary>
    private static void TryOpen(MediaPlayer player, string path)
    {
        try
        {
            if (File.Exists(path))
            {
                player.Open(new Uri(path, UriKind.Absolute));
            }
        }
        catch (Exception ex) when (ex is UriFormatException or IOException or UnauthorizedAccessException)
        {
            // Leaves the player with nothing open - Play() then does nothing, which is the intent.
        }
    }

    /// <summary>0.0-1.0, applied to both cues - there's one shared slider in Settings, not a per-cue one,
    /// since these are two variants of the same "app made a sound" concept rather than independently
    /// tunable channels.</summary>
    public void SetVolume(double volume)
    {
        var clamped = Math.Clamp(volume, 0.0, 1.0);
        _clipSavedPlayer.Volume = clamped;
        _recordingStartedPlayer.Volume = clamped;
    }

    /// <summary>
    /// Stop() before Play() rather than just Play(): MediaPlayer doesn't rewind on its own, so without this
    /// a clip saved again before the previous cue finished would resume mid-clip (or play nothing, already
    /// at the end) instead of restarting the sound from the top.
    /// </summary>
    public void PlayClipSaved()
    {
        _clipSavedPlayer.Stop();
        _clipSavedPlayer.Play();
    }

    public void PlayRecordingStarted()
    {
        _recordingStartedPlayer.Stop();
        _recordingStartedPlayer.Play();
    }

    /// <summary>
    /// Locates assets\Sounds\{fileName} beside the exe (packaged install) or at the repo root (running
    /// from a bin output folder) - see <see cref="BundledResources"/>. A missing file yields the
    /// install-relative path, which <see cref="TryOpen"/> then declines to open.
    /// </summary>
    private static string LocateSoundFile(string fileName)
    {
        BundledResources.TryResolve(out var path, "assets", "Sounds", fileName);
        return path;
    }

    /// <summary>Releases both players' file handles - without this the bundled WAVs stay locked for the
    /// process lifetime, which blocks the "replace either file in place" swap described above.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clipSavedPlayer.Close();
        _recordingStartedPlayer.Close();
        GC.SuppressFinalize(this);
    }
}
