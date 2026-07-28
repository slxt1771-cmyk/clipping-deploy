using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using ClippingSoftware.Core.Recording;
using ClippingSoftware.Data.ClipLibrary;
using ClippingSoftware.Data.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClippingSoftware.App.ViewModels;

/// <summary>
/// Backs the clip library browser. Loads persisted clips from <see cref="ClipRepository"/> and,
/// when a <see cref="RecordingIngestCoordinator"/> is supplied, subscribes to its ClipIngested event so
/// newly recorded clips (including trimmed exports made from the Trim Editor, which route through the
/// same coordinator) appear live at the front of the grid without a manual refresh.
/// </summary>
public partial class ClipBrowserViewModel : ObservableObject, IDisposable
{
    private readonly ClipRepository _clipRepository;
    private readonly TagRepository _tagRepository;
    private readonly RecordingIngestCoordinator? _ingestCoordinator;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Drives the FILTERS popup's open/closed state (bound straight to a ToggleButton -
    /// no command needed for a plain open/close toggle).</summary>
    [ObservableProperty]
    private bool _isFilterPanelOpen;

    /// <summary>"All"/"Clips"/"Recordings" - narrows FilterClip by <see cref="ClipMetadata.IsTrimmedCopy"/>
    /// alongside the existing search/tag filters, so a user can tell a Trim Editor export apart from a raw
    /// replay-buffer/manual recording. Same plain-string convention as CurrentView (see its doc comment).</summary>
    [ObservableProperty]
    private string _typeFilter = "All";

    /// <summary>Whether any filter (type or tag) is currently narrowing the library, so the FILTERS button
    /// can show an active state instead of looking identical whether or not something's applied.</summary>
    public bool HasActiveFilters => TypeFilter != "All" || AllTags.Any(t => t.IsSelected);

    public ObservableCollection<ClipItemViewModel> Clips { get; } = [];

    /// <summary>
    /// A live filtered view over <see cref="Clips"/>, keyed off <see cref="SearchText"/> (matched
    /// against the detected game name and the clip's file name). The search box in ClipBrowserView
    /// binds to this rather than to Clips directly, so filtering doesn't touch the underlying
    /// collection (deleting/inserting items while a filter is active still works unmodified).
    /// </summary>
    public ICollectionView ClipsView => _clipsView ??= BuildClipsView();

    private ICollectionView? _clipsView;

    /// <summary>
    /// Raised when the user invokes "Open in Trim Editor" for a clip, or switches clips via the
    /// switcher inside the embedded editor (see OnEditingClipChanged - same event, same path). This view
    /// model has no reference to RecordingIngestCoordinator/export-folder settings, so it just raises the
    /// event with the target clip; MainViewModel (which owns both this ClipBrowserViewModel and those
    /// dependencies) subscribes and constructs the actual TrimEditorViewModel into ActiveEditor.
    /// </summary>
    public event Action<ClipItemViewModel>? TrimEditorRequested;

    /// <summary>Which of the Clips tab's sub-views is showing - "Library"/"Edit" (the multi-clip combine+
    /// trim editor, what used to be a separate "Sequence" tab)/"ClipEditor" (a single clip's own trim
    /// editor - a different system, opened per-clip)/"Tags" (the tag manager). A plain string rather than a
    /// C# enum to match this codebase's existing convention for XAML-facing mode switches (see
    /// GameProfile.CaptureMode) and its simple DataTrigger-by-Value idiom.</summary>
    [ObservableProperty]
    private string _currentView = "Library";

    /// <summary>The clip currently open in the embedded editor - also drives the switcher ComboBox inside
    /// TrimEditorView, so picking a different clip there goes through the same OnEditingClipChanged path
    /// as clicking "Open in Trim Editor" from the library grid.</summary>
    [ObservableProperty]
    private ClipItemViewModel? _editingClip;

    /// <summary>Constructed by MainViewModel (via TrimEditorRequested) and bound as the embedded editor's
    /// DataContext; null (and TrimEditorView's MediaElement released - see its DataContextChanged) while
    /// on the Library or Sequence sub-view.</summary>
    [ObservableProperty]
    private TrimEditorViewModel? _activeEditor;

    /// <summary>The one persistent multi-clip workspace behind the EDIT nav pill - see
    /// SequenceEditorViewModel's doc comment for why there's exactly one rather than a list of saved
    /// projects. Still named "Sequence" internally (the class/table names never changed, only the outward
    /// "Sequence" tab/label did) - see ClipBrowserView.xaml's EDIT sub-view comment.</summary>
    public SequenceEditorViewModel Sequence { get; }

    /// <summary>Every tag in the system, backing both the Library's filter-chip row (TagViewModel.IsSelected
    /// there means "part of the active AND filter") and TagManagerViewModel's own list - kept in sync with
    /// it via TagsChanged rather than being two independent collections.</summary>
    public ObservableCollection<TagViewModel> AllTags { get; } = [];

    public TagManagerViewModel TagManager { get; }

    public ClipBrowserViewModel(
        ClipRepository clipRepository,
        TagRepository tagRepository,
        SequenceEditorViewModel sequenceEditor,
        RecordingIngestCoordinator? ingestCoordinator = null)
    {
        _clipRepository = clipRepository;
        _tagRepository = tagRepository;
        _ingestCoordinator = ingestCoordinator;
        Sequence = sequenceEditor;
        TagManager = new TagManagerViewModel(tagRepository, () => Clips);
        TagManager.TagsChanged += RefreshTags;

        if (_ingestCoordinator is not null)
        {
            _ingestCoordinator.ClipIngested += OnClipIngested;
        }
    }

    partial void OnEditingClipChanged(ClipItemViewModel? value)
    {
        if (value is not null)
        {
            TrimEditorRequested?.Invoke(value);
        }
    }

    [RelayCommand]
    private void BackToLibrary()
    {
        ActiveEditor = null;
        EditingClip = null;
        CurrentView = "Library";
    }

    /// <summary>Shows the multi-clip combine+trim editor (the EDIT nav pill) - named for what it does now,
    /// not what it used to be called; CurrentView's own value stays "Edit" for the same reason.</summary>
    [RelayCommand]
    private void ShowMultiClipEditor() => CurrentView = "Edit";

    [RelayCommand]
    private void ShowTagManager() => CurrentView = "Tags";

    /// <summary>Toggles one tag in/out of the active AND filter (see FilterClip) and re-applies it - clicking
    /// a second chip narrows further rather than replacing the first, matching the "green AND Rust" example
    /// this feature was requested for.</summary>
    [RelayCommand]
    private void ToggleTagFilter(TagViewModel? tag)
    {
        if (tag is null)
        {
            return;
        }

        tag.IsSelected = !tag.IsSelected;
        ClipsView.Refresh();
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    [RelayCommand]
    private void ToggleFilterPanel() => IsFilterPanelOpen = !IsFilterPanelOpen;

    /// <summary>Backs the FILTERS popup's ALL/CLIPS/RECORDINGS segmented control.</summary>
    [RelayCommand]
    private void SetTypeFilter(string? type)
    {
        TypeFilter = type ?? "All";
    }

    /// <summary>Clears both the tag AND-filter and the type filter in one action, so the FILTERS popup
    /// doesn't require unchecking each chip individually.</summary>
    [RelayCommand]
    private void ClearFilters()
    {
        foreach (var tag in AllTags)
        {
            tag.IsSelected = false;
        }

        TypeFilter = "All";
        ClipsView.Refresh();
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    partial void OnTypeFilterChanged(string value)
    {
        ClipsView.Refresh();
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    /// <summary>Loads (or reloads) the full clip list from the repository, newest first, along with every
    /// clip's assigned tags.</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = null;
        try
        {
            var clips = await Task.Run(() => _clipRepository.GetAll());
            var tagsByClip = await Task.Run(() => _tagRepository.GetTagsByClip());

            Clips.Clear();
            foreach (var clip in clips)
            {
                var item = new ClipItemViewModel(clip);
                if (tagsByClip.TryGetValue(clip.Id, out var tags))
                {
                    foreach (var tag in tags)
                    {
                        item.Tags.Add(tag);
                    }
                }
                Clips.Add(item);
            }

            RefreshTags();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load clips: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Reloads AllTags (preserving which chips were active in the filter) and every visible clip's
    /// Tags from the repository - called after any create/rename/recolor/delete/assign change from
    /// TagManagerViewModel, and once from LoadAsync. Re-fetching wholesale rather than patching in-memory
    /// objects is simpler and can't drift out of sync with what TagManagerViewModel just wrote.</summary>
    private void RefreshTags()
    {
        var selectedIds = AllTags.Where(t => t.IsSelected).Select(t => t.Id).ToHashSet();

        AllTags.Clear();
        foreach (var tag in _tagRepository.GetAll())
        {
            AllTags.Add(new TagViewModel(tag) { IsSelected = selectedIds.Contains(tag.Id) });
        }

        var tagsByClip = _tagRepository.GetTagsByClip();
        foreach (var clip in Clips)
        {
            clip.Tags.Clear();
            if (tagsByClip.TryGetValue(clip.Id, out var tags))
            {
                foreach (var tag in tags)
                {
                    clip.Tags.Add(tag);
                }
            }
        }

        ClipsView.Refresh();
    }

    private void OnClipIngested(ClipMetadata clip)
    {
        RunOnUiThread(() =>
        {
            var item = new ClipItemViewModel(clip);
            // The auto-tag-by-game link (if any) was already written by RecordingIngestCoordinator before
            // this event fired, so it's safe to look it up here rather than leaving the new card untagged
            // until the next full Refresh.
            foreach (var tag in _tagRepository.GetTagsByClip().GetValueOrDefault(clip.Id, []))
            {
                item.Tags.Add(tag);
            }
            Clips.Insert(0, item);
        });
    }

    private ICollectionView BuildClipsView()
    {
        var view = CollectionViewSource.GetDefaultView(Clips);
        view.Filter = FilterClip;
        return view;
    }

    private bool FilterClip(object item)
    {
        if (item is not ClipItemViewModel clip)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim();
            var matchesSearch = clip.GameDisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(clip.FilePath).Contains(needle, StringComparison.OrdinalIgnoreCase);
            if (!matchesSearch)
            {
                return false;
            }
        }

        var activeTagIds = AllTags.Where(t => t.IsSelected).Select(t => t.Id).ToList();
        if (activeTagIds.Count > 0 && !activeTagIds.All(id => clip.Tags.Any(t => t.Id == id)))
        {
            return false;
        }

        var matchesType = TypeFilter switch
        {
            "Clips" => clip.Clip.IsTrimmedCopy,
            "Recordings" => !clip.Clip.IsTrimmedCopy,
            _ => true,
        };
        if (!matchesType)
        {
            return false;
        }

        return true;
    }

    partial void OnSearchTextChanged(string value) => ClipsView.Refresh();

    [RelayCommand]
    private async Task Refresh() => await LoadAsync();

    [RelayCommand]
    private void OpenInExplorer(ClipItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            if (File.Exists(item.FilePath))
            {
                // Path.GetFullPath normalizes to native backslash separators: FilePath as stored came
                // straight from OBS-websocket's own path string, and OBS (cross-platform under the hood)
                // reports paths with forward slashes even on Windows. .NET's own file APIs don't care
                // either way, but explorer.exe's /select argument apparently does - a forward-slash path
                // there silently fails to resolve to the specific file and Explorer falls back to its
                // default landing location instead of the clip's actual folder with the file selected.
                var explorerPath = Path.GetFullPath(item.FilePath);

                // UseShellExecute must be explicit here: .NET's UseShellExecute default is false (a
                // Framework->Core breaking change), and without it explorer.exe launches as a bare child
                // process instead of being handed off to the existing shell instance via ShellExecute -
                // /select then gets silently ignored and Explorer opens to its own default landing view
                // instead of the clip's actual folder with the file selected.
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{explorerPath}\"")
                {
                    UseShellExecute = true,
                });
            }
            else
            {
                StatusMessage = "Clip file no longer exists on disk.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open Explorer: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Delete(ClipItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            if (File.Exists(item.FilePath))
            {
                File.Delete(item.FilePath);
            }

            if (!string.IsNullOrEmpty(item.ThumbnailPath) && File.Exists(item.ThumbnailPath))
            {
                File.Delete(item.ThumbnailPath);
            }

            // Extracted per-track audio files (M11) live alongside the thumbnail, not next to the video -
            // same cleanup obligation, just a variable-length list instead of one fixed path.
            foreach (var track in item.Clip.AudioTracks)
            {
                if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
                {
                    File.Delete(track.FilePath);
                }
            }

            _clipRepository.Delete(item.Id);
            _tagRepository.RemoveAllForClip(item.Id);
            Clips.Remove(item);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to delete clip: {ex.Message}";
        }
    }

    /// <summary>Switches to this clip's own trim editor - a different system from the EDIT nav pill's
    /// multi-clip combiner (see CurrentView's doc comment); setting EditingClip drives both the switch and
    /// TrimEditorRequested, see OnEditingClipChanged.</summary>
    [RelayCommand]
    private void OpenInTrimEditor(ClipItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        CurrentView = "ClipEditor";
        EditingClip = item;
    }

    /// <summary>Adds a clip to the persistent multi-clip workspace behind the EDIT nav pill - the whole
    /// clip's duration by default, trimmable afterward from that sub-view.</summary>
    [RelayCommand]
    private void AddToSequence(ClipItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        Sequence.AddClip(item.Clip);
        StatusMessage = $"Added to edit: {Path.GetFileName(item.FilePath)}";
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    public void Dispose()
    {
        if (_ingestCoordinator is not null)
        {
            _ingestCoordinator.ClipIngested -= OnClipIngested;
        }

        TagManager.TagsChanged -= RefreshTags;

        GC.SuppressFinalize(this);
    }
}
