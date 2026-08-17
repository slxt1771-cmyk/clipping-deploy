using System.Collections.ObjectModel;
using ClippingSoftware.Core.Obs;
using ClippingSoftware.Data.ClipLibrary;
using ClippingSoftware.Data.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClippingSoftware.App.ViewModels;

/// <summary>
/// Backs the Game Profiles panel (embedded in the Settings tab): lists <see cref="GameProfile"/> rows from
/// <see cref="GameProfileRepository"/> and supports add/edit/delete. This is the piece that makes
/// GameDetectionService/ProfileApplier's per-game switching actually usable day-to-day - without it, only
/// the single seeded IsBuiltInDefault profile can ever exist, since the repository has no other caller that
/// inserts rows.
///
/// Editing works against a set of Edit* draft properties that mirror the selected profile rather than
/// binding the form directly to GameProfile's own properties, because GameProfile is a plain data model
/// with no INotifyPropertyChanged - mutating it in place wouldn't refresh the bound DataGrid row, and
/// binding directly would also let unsaved edits leak into the list before Save is clicked.
/// </summary>
public partial class GameProfilesViewModel : ObservableObject
{
    private readonly GameProfileRepository _gameProfileRepository;
    private readonly ObsController? _obsController;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<GameProfile> Profiles { get; } = [];

    /// <summary>
    /// Populated from OBS's live profile list when connected (see <see cref="RefreshObsProfiles"/>);
    /// stays empty otherwise. The view's ObsProfileName combo box is editable, so an empty list just
    /// makes it behave like a plain text box - free text is always an acceptable fallback.
    /// </summary>
    public ObservableCollection<string> ObsProfileNameOptions { get; } = [];

    [ObservableProperty]
    private GameProfile? _selectedProfile;

    [ObservableProperty]
    private string _editDisplayName = string.Empty;

    /// <summary>Newline/comma-separated editable form of GameProfile.ExecutableMatches; split/joined on load/save.</summary>
    [ObservableProperty]
    private string _editExecutableMatchesText = string.Empty;

    [ObservableProperty]
    private bool _editRequireFullscreenOrBorderless;

    [ObservableProperty]
    private string _editCaptureMode = "DisplayCapture";

    [ObservableProperty]
    private CaptureOptionItem? _editCaptureTargetMonitor;

    [ObservableProperty]
    private CaptureOptionItem? _editCaptureTargetWindow;

    /// <summary>
    /// Populated from ObsController.GetMonitorOptions/GetWindowOptions when connected (see
    /// RefreshCaptureOptions) - same live-enumeration source MainViewModel's Capture-tab pickers use,
    /// reused here so per-profile capture targets are chosen from a picker rather than typed by hand.
    /// </summary>
    public ObservableCollection<CaptureOptionItem> MonitorOptions { get; } = [];

    public ObservableCollection<CaptureOptionItem> WindowOptions { get; } = [];

    [ObservableProperty]
    private bool _editAudioEnabled = true;

    [ObservableProperty]
    private int _editOutputWidth;

    [ObservableProperty]
    private int _editOutputHeight;

    /// <summary>When true, Output Width/Height are ignored and ProfileApplier resolves the actual canvas
    /// size from the current display mode at apply-time instead (see GameProfile.AutoDetectResolution).</summary>
    [ObservableProperty]
    private bool _editAutoDetectResolution;

    [ObservableProperty]
    private int _editFps = 60;

    [ObservableProperty]
    private string _editObsProfileName = string.Empty;

    [ObservableProperty]
    private string _editCodec = "hevc";

    [ObservableProperty]
    private string _editRateControl = "CQP";

    [ObservableProperty]
    private int _editCqLevel = 14;

    [ObservableProperty]
    private string _editPreset = "p6";

    [ObservableProperty]
    private string _editTuning = "hq";

    [ObservableProperty]
    private string _editMultipass = "qres";

    [ObservableProperty]
    private int _editKeyframeIntervalSec = 2;

    /// <param name="obsController">
    /// Optional so this view model stays constructible/testable without a live OBS connection; MainViewModel
    /// passes its real ObsController so the ObsProfileName dropdown can populate from OBS's actual profiles.
    /// </param>
    public GameProfilesViewModel(GameProfileRepository gameProfileRepository, ObsController? obsController = null)
    {
        _gameProfileRepository = gameProfileRepository;
        _obsController = obsController;
    }

    /// <summary>Loads (or reloads) the full profile list from the repository, built-in default first.</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = null;
        try
        {
            var profiles = await Task.Run(() => _gameProfileRepository.GetAll());

            Profiles.Clear();
            foreach (var profile in profiles.OrderByDescending(p => p.IsBuiltInDefault).ThenBy(p => p.DisplayName))
            {
                Profiles.Add(profile);
            }

            RefreshObsProfiles();
            RefreshCaptureOptions();

            SelectedProfile = Profiles.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load game profiles: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedProfileChanged(GameProfile? value)
    {
        LoadDraftFrom(value);
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private void LoadDraftFrom(GameProfile? profile)
    {
        profile ??= new GameProfile();

        EditDisplayName = profile.DisplayName;
        EditExecutableMatchesText = string.Join(Environment.NewLine, profile.ExecutableMatches);
        EditRequireFullscreenOrBorderless = profile.RequireFullscreenOrBorderless;
        EditCaptureMode = profile.CaptureMode;
        EditCaptureTargetMonitor = MonitorOptions.FirstOrDefault(m => m.Value == profile.CaptureTargetMonitorId);
        EditCaptureTargetWindow = WindowOptions.FirstOrDefault(w => w.Value == profile.CaptureTargetWindow);
        EditAudioEnabled = profile.AudioEnabled;
        EditOutputWidth = profile.OutputWidth;
        EditOutputHeight = profile.OutputHeight;
        EditAutoDetectResolution = profile.AutoDetectResolution;
        EditFps = profile.Fps;
        EditObsProfileName = profile.ObsProfileName;
        EditCodec = profile.Encoder.Codec;
        EditRateControl = profile.Encoder.RateControl;
        EditCqLevel = profile.Encoder.CqLevel;
        EditPreset = profile.Encoder.Preset;
        EditTuning = profile.Encoder.Tuning;
        EditMultipass = profile.Encoder.Multipass;
        EditKeyframeIntervalSec = profile.Encoder.KeyframeIntervalSec;
    }

    /// <summary>
    /// Inserts a new profile immediately (rather than only staging it in memory) so it survives even if
    /// the user navigates away before hitting Save; OutputWidth/Height are seeded to match the
    /// currently-provisioned "Untitled" OBS profile (see GameProfileRepository.BuildDefaultProfile) since
    /// 0x0 isn't a usable resolution - everything else is left as GameProfile's own field defaults.
    /// </summary>
    [RelayCommand]
    private void Add()
    {
        var profile = new GameProfile
        {
            DisplayName = "New Profile",
            ExecutableMatches = [],
            OutputWidth = 2560,
            OutputHeight = 1440,
            IsBuiltInDefault = false,
        };

        try
        {
            _gameProfileRepository.Insert(profile);
            Profiles.Add(profile);
            SelectedProfile = profile;
            StatusMessage = "Added new profile - fill in its executable match(es) and Save.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to add profile: {ex.Message}";
        }
    }

    /// <summary>
    /// Builds a fresh GameProfile from the Edit* draft fields and persists it, then swaps the list entry
    /// for the new instance (a plain list-index replace, since GameProfile has no change notification of
    /// its own for the bound DataGrid to pick up otherwise).
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        try
        {
            var updated = BuildProfileFromDraft(SelectedProfile);
            _gameProfileRepository.Update(updated);

            var index = Profiles.IndexOf(SelectedProfile);
            if (index >= 0)
            {
                Profiles[index] = updated;
            }

            SelectedProfile = updated;
            StatusMessage = $"Saved \"{updated.DisplayName}\".";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save profile: {ex.Message}";
        }
    }

    private GameProfile BuildProfileFromDraft(GameProfile source) => new()
    {
        Id = source.Id,
        DisplayName = EditDisplayName,
        ExecutableMatches = EditExecutableMatchesText
            .Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList(),
        RequireFullscreenOrBorderless = EditRequireFullscreenOrBorderless,
        CaptureMode = EditCaptureMode,
        CaptureTargetMonitorId = EditCaptureTargetMonitor?.Value,
        CaptureTargetWindow = EditCaptureTargetWindow?.Value,
        AudioEnabled = EditAudioEnabled,
        OutputWidth = EditOutputWidth,
        OutputHeight = EditOutputHeight,
        AutoDetectResolution = EditAutoDetectResolution,
        Fps = EditFps,
        ObsProfileName = EditObsProfileName,
        Encoder = new NvencSettings
        {
            Codec = EditCodec,
            RateControl = EditRateControl,
            CqLevel = EditCqLevel,
            Preset = EditPreset,
            Tuning = EditTuning,
            Multipass = EditMultipass,
            KeyframeIntervalSec = EditKeyframeIntervalSec,
        },
        IsBuiltInDefault = source.IsBuiltInDefault,
    };

    /// <summary>Disabled for the built-in default in the view model as well as in the UI - don't rely solely
    /// on GameProfileRepository.Delete's own IsBuiltInDefault guard.</summary>
    private bool CanDelete() => SelectedProfile is { IsBuiltInDefault: false };

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (!CanDelete() || SelectedProfile is null)
        {
            return;
        }

        try
        {
            _gameProfileRepository.Delete(SelectedProfile.Id);
            Profiles.Remove(SelectedProfile);
            SelectedProfile = Profiles.FirstOrDefault();
            StatusMessage = "Profile deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to delete profile: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task Refresh() => await LoadAsync();

    /// <summary>
    /// Best-effort refresh of ObsProfileNameOptions from ObsController.GetProfileNames(). Called on load
    /// and whenever MainViewModel's ObsController reconnects; swallows failures since this is a UI
    /// convenience, not something that should ever block editing a profile.
    /// </summary>
    [RelayCommand]
    private void RefreshObsProfiles()
    {
        ObsProfileNameOptions.Clear();
        try
        {
            if (_obsController is { IsConnected: true })
            {
                foreach (var name in _obsController.GetProfileNames())
                {
                    ObsProfileNameOptions.Add(name);
                }
            }
        }
        catch
        {
            // Best-effort convenience only - an empty list just means the combo box behaves like a text box.
        }
    }

    /// <summary>
    /// Best-effort refresh of MonitorOptions/WindowOptions (M7), mirroring RefreshObsProfiles above. Called
    /// on load and whenever MainViewModel's ObsController reconnects. Re-applies the current draft's
    /// selection afterward since Clear() would otherwise drop it even though the underlying
    /// CaptureTargetMonitorId/CaptureTargetWindow value hasn't changed.
    /// </summary>
    [RelayCommand]
    private void RefreshCaptureOptions()
    {
        var previousMonitorValue = EditCaptureTargetMonitor?.Value;
        var previousWindowValue = EditCaptureTargetWindow?.Value;

        MonitorOptions.Clear();
        WindowOptions.Clear();
        try
        {
            if (_obsController is { IsConnected: true })
            {
                foreach (var (label, value) in _obsController.GetMonitorOptions(Core.Obs.ObsController.DisplayCaptureSourceName))
                {
                    MonitorOptions.Add(new CaptureOptionItem(label, value));
                }

                foreach (var (label, value) in _obsController.GetWindowOptions(Core.Obs.ObsController.WindowCaptureSourceName))
                {
                    WindowOptions.Add(new CaptureOptionItem(label, value));
                }
            }
        }
        catch
        {
            // Best-effort convenience only, same tolerance as RefreshObsProfiles.
        }

        EditCaptureTargetMonitor = MonitorOptions.FirstOrDefault(m => m.Value == previousMonitorValue);
        EditCaptureTargetWindow = WindowOptions.FirstOrDefault(w => w.Value == previousWindowValue);
    }
}
