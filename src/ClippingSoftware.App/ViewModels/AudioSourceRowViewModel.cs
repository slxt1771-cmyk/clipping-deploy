using ClippingSoftware.Core.Recording;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClippingSoftware.App.ViewModels;

/// <summary>
/// Binding-friendly wrapper around one <see cref="AudioSourceInfo"/> for the Settings tab's AUDIO SOURCES
/// list. AudioSourceInfo itself is an immutable record (AudioSourceManager rebuilds the whole list on any
/// change), so the mute checkbox/volume slider need their own bindable/settable properties rather than
/// binding straight to the record - these are those properties, wired back to
/// AudioSourceManager.SetMuted/SetVolume on change.
/// </summary>
public partial class AudioSourceRowViewModel : ObservableObject
{
    private readonly AudioSourceManager _audioSourceManager;

    public string InputName { get; }
    public string DisplayLabel { get; }
    public int TrackNumber { get; }
    public bool IsBuiltIn { get; }

    [ObservableProperty]
    private bool _isMuted;

    /// <summary>0-100, mirroring OBS's own volume-slider convention. Stored/sent as a linear multiplier
    /// (VolumePercent / 100.0) - see ObsController.SetInputVolume.</summary>
    [ObservableProperty]
    private int _volumePercent;

    public AudioSourceRowViewModel(AudioSourceInfo source, bool isMuted, int volumePercent, AudioSourceManager audioSourceManager)
    {
        _audioSourceManager = audioSourceManager;
        InputName = source.InputName;
        DisplayLabel = source.DisplayLabel;
        TrackNumber = source.TrackNumber;
        IsBuiltIn = source.IsBuiltIn;
        _isMuted = isMuted;
        _volumePercent = volumePercent;
    }

    partial void OnIsMutedChanged(bool value) => _audioSourceManager.SetMuted(InputName, value);

    partial void OnVolumePercentChanged(int value) => _audioSourceManager.SetVolume(InputName, value / 100.0);
}
