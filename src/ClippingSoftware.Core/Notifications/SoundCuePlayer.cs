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
public class SoundCuePlayer
{
    private readonly MediaPlayer _clipSavedPlayer = new();
    private readonly MediaPlayer _recordingStartedPlayer = new();

    public SoundCuePlayer(string? clipSavedSoundPath = null, string? recordingStartedSoundPath = null)
    {
        _clipSavedPlayer.Open(new Uri(clipSavedSoundPath ?? LocateSoundFile("clip-saved.wav")));
        _recordingStartedPlayer.Open(new Uri(recordingStartedSoundPath ?? LocateSoundFile("recording-started.wav")));
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
    /// Walks up from the app's base directory looking for assets\Sounds\{fileName} (same convention as
    /// GameDatabase.LocateKnownGamesFile/FfmpegTools.LocateToolsDirectory), falling back to the known
    /// absolute repo path for this single-machine install if the walk-up fails.
    /// </summary>
    private static string LocateSoundFile(string fileName)
    {
        var relative = Path.Combine("assets", "Sounds", fileName);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Path.Combine(@"D:\claude stuff\clipping software", relative);
    }
}
