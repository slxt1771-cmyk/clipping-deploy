using System.Collections.ObjectModel;
using System.Linq;
using ClippingSoftware.Data.ClipLibrary;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClippingSoftware.App.ViewModels;

/// <summary>One row in the selected tag's clip checklist - IsAssigned toggling writes straight through to
/// TagRepository via TagManagerViewModel's PropertyChanged subscription (same "no separate save button"
/// pattern as AudioSourceManager's mute checkboxes).</summary>
public partial class TagClipRowViewModel(ClipItemViewModel clip, bool isAssigned) : ObservableObject
{
    public ClipItemViewModel Clip { get; } = clip;
    public string FileNameTag => Clip.FileNameTag;

    [ObservableProperty]
    private bool _isAssigned = isAssigned;
}

/// <summary>
/// Backs the Clips tab's TAGS sub-view: a master-detail screen (same shape as GameProfilesView) for
/// creating/recoloring/renaming/deleting tags on the left, and - once a tag is selected - a checklist of
/// every clip on the right to assign/unassign that tag. This is also the only place manual tag *assignment*
/// happens; a clip card only shows its tags read-only (see ClipItemViewModel.Tags).
///
/// Auto-tagging by detected game/app happens separately, in RecordingIngestCoordinator at ingest time (see
/// TagRepository.GetOrCreateByName) - this view model only deals with tags the user is looking at/editing.
/// </summary>
public partial class TagManagerViewModel : ObservableObject
{
    private readonly TagRepository _tagRepository;
    private readonly Func<IReadOnlyList<ClipItemViewModel>> _getClips;

    public ObservableCollection<TagViewModel> Tags { get; } = [];

    public ObservableCollection<TagClipRowViewModel> ClipRows { get; } = [];

    [ObservableProperty]
    private TagViewModel? _selectedTag;

    [ObservableProperty]
    private string _newTagName = string.Empty;

    [ObservableProperty]
    private string _newTagColorHex = "#8A8A90";

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Raised after any create/rename/recolor/delete/assign change, so ClipBrowserViewModel can
    /// refresh the filter-chip row and every clip card's tag chips from the repository - simpler and more
    /// robust than trying to patch the in-memory copies each card already holds.</summary>
    public event Action? TagsChanged;

    public TagManagerViewModel(TagRepository tagRepository, Func<IReadOnlyList<ClipItemViewModel>> getClips)
    {
        _tagRepository = tagRepository;
        _getClips = getClips;
        Reload();
    }

    public void Reload()
    {
        Tags.Clear();
        foreach (var tag in _tagRepository.GetAll())
        {
            Tags.Add(new TagViewModel(tag));
        }
    }

    /// <summary>Drives the TAGS view's placeholder-vs-checklist visibility (via BooleanToVisibilityConverter)
    /// - not an [ObservableProperty] itself since it's derived from SelectedTag, not an independent field.</summary>
    public bool HasSelectedTag => SelectedTag is not null;

    partial void OnSelectedTagChanged(TagViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedTag));
        RebuildClipRows();
    }

    [RelayCommand]
    private void SelectTag(TagViewModel? tag) => SelectedTag = tag;

    private void RebuildClipRows()
    {
        foreach (var row in ClipRows)
        {
            row.PropertyChanged -= OnClipRowPropertyChanged;
        }
        ClipRows.Clear();

        if (SelectedTag is null)
        {
            return;
        }

        foreach (var clip in _getClips())
        {
            var row = new TagClipRowViewModel(clip, clip.Tags.Any(t => t.Id == SelectedTag.Id));
            row.PropertyChanged += OnClipRowPropertyChanged;
            ClipRows.Add(row);
        }
    }

    private void OnClipRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TagClipRowViewModel.IsAssigned) || sender is not TagClipRowViewModel row || SelectedTag is null)
        {
            return;
        }

        if (row.IsAssigned)
        {
            _tagRepository.AddClipTag(row.Clip.Id, SelectedTag.Id);
        }
        else
        {
            _tagRepository.RemoveClipTag(row.Clip.Id, SelectedTag.Id);
        }

        TagsChanged?.Invoke();
    }

    [RelayCommand]
    private void AddTag()
    {
        if (string.IsNullOrWhiteSpace(NewTagName))
        {
            StatusMessage = "Enter a tag name.";
            return;
        }

        var colorHex = string.IsNullOrWhiteSpace(NewTagColorHex) ? "#8A8A90" : NewTagColorHex;
        var tag = _tagRepository.Add(NewTagName.Trim(), colorHex);
        Tags.Add(new TagViewModel(tag));
        NewTagName = string.Empty;
        StatusMessage = null;
        TagsChanged?.Invoke();
    }

    [RelayCommand]
    private void SaveTag(TagViewModel? tag)
    {
        if (tag is null)
        {
            return;
        }

        _tagRepository.Update(tag.ToModel());
        TagsChanged?.Invoke();
    }

    [RelayCommand]
    private void DeleteTag(TagViewModel? tag)
    {
        if (tag is null)
        {
            return;
        }

        _tagRepository.Delete(tag.Id);
        Tags.Remove(tag);
        if (SelectedTag == tag)
        {
            SelectedTag = null;
        }

        TagsChanged?.Invoke();
    }
}
