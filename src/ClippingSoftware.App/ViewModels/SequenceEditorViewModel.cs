using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ClippingSoftware.Core.Recording;
using ClippingSoftware.Data.ClipLibrary;
using ClippingSoftware.Data.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClippingSoftware.App.ViewModels;

/// <summary>Binding-friendly wrapper around one SequenceClip + its resolved ClipMetadata, for the
/// Sequence sub-view's ordered list. In/out point edits write through to SequenceRepository immediately
/// (see SequenceEditorViewModel's PropertyChanged subscription) - same "no separate draft/autosave
/// mechanism" reasoning as AudioSourceManager: the persisted row already reflects current state.</summary>
public partial class SequenceClipRowViewModel(SequenceClip record, ClipMetadata clip) : ObservableObject
{
    public Guid Id { get; } = record.Id;
    public ClipMetadata Clip { get; } = clip;
    public string FileNameTag => Path.GetFileName(Clip.FilePath).ToUpperInvariant();

    [ObservableProperty]
    private double _inPointSeconds = record.InPointSeconds;

    [ObservableProperty]
    private double _outPointSeconds = record.OutPointSeconds;
}

/// <summary>
/// Backs the Clips tab's "Sequence" sub-view (M16): a deliberately simple alternative to the single-clip
/// editor - pick several clips, trim each one's in/out range, reorder them, and export the whole thing as
/// one combined file (via Core.Recording.SequenceExporter). There is exactly one sequence at a time (a
/// scratch workspace, not a project manager with multiple named/saved sequences) - SequenceRepository's
/// rows already persist on every add/remove/reorder/trim change, so there's no separate "save project"
/// step and no lost progress on restart, matching the same principle the single-clip editor's draft
/// system uses for the same reason.
/// </summary>
public partial class SequenceEditorViewModel : ObservableObject
{
    private readonly SequenceRepository _sequenceRepository;
    private readonly ClipRepository _clipRepository;
    private readonly string _exportStorageFolder;
    private readonly RecordingIngestCoordinator? _ingestCoordinator;
    private readonly SequenceExporter _sequenceExporter;

    public ObservableCollection<SequenceClipRowViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private string? _statusMessage;

    public SequenceEditorViewModel(
        SequenceRepository sequenceRepository,
        ClipRepository clipRepository,
        string exportStorageFolder,
        RecordingIngestCoordinator? ingestCoordinator = null,
        SequenceExporter? sequenceExporter = null)
    {
        _sequenceRepository = sequenceRepository;
        _clipRepository = clipRepository;
        _exportStorageFolder = exportStorageFolder;
        _ingestCoordinator = ingestCoordinator;
        _sequenceExporter = sequenceExporter ?? new SequenceExporter();

        foreach (var record in _sequenceRepository.GetAll())
        {
            var clip = _clipRepository.GetById(record.ClipId);
            if (clip is not null)
            {
                Items.Add(CreateRow(record, clip));
            }
            // A record whose clip was deleted from the library independently is silently skipped - the
            // orphaned row is left in the DB rather than guessed-at; ClearAll is the way to reset it.
        }
    }

    private SequenceClipRowViewModel CreateRow(SequenceClip record, ClipMetadata clip)
    {
        var row = new SequenceClipRowViewModel(record, clip);
        row.PropertyChanged += (_, _) => _sequenceRepository.UpdateTrim(row.Id, row.InPointSeconds, row.OutPointSeconds);
        return row;
    }

    /// <summary>Appends a clip to the end of the sequence at its full duration, trimmable afterward.</summary>
    public void AddClip(ClipMetadata clip)
    {
        var record = new SequenceClip
        {
            ClipId = clip.Id,
            OrderIndex = Items.Count,
            InPointSeconds = 0,
            OutPointSeconds = clip.Duration.TotalSeconds,
        };
        _sequenceRepository.Add(record);
        Items.Add(CreateRow(record, clip));
    }

    [RelayCommand]
    private void RemoveClip(SequenceClipRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        _sequenceRepository.Remove(row.Id);
        Items.Remove(row);
        PersistOrder();
    }

    [RelayCommand]
    private void MoveUp(SequenceClipRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var index = Items.IndexOf(row);
        if (index > 0)
        {
            Items.Move(index, index - 1);
            PersistOrder();
        }
    }

    [RelayCommand]
    private void MoveDown(SequenceClipRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var index = Items.IndexOf(row);
        if (index >= 0 && index < Items.Count - 1)
        {
            Items.Move(index, index + 1);
            PersistOrder();
        }
    }

    [RelayCommand]
    private void ClearAll()
    {
        _sequenceRepository.Clear();
        Items.Clear();
    }

    private void PersistOrder() =>
        _sequenceRepository.UpdateOrder(Items.Select(row => new SequenceClip
        {
            Id = row.Id,
            ClipId = row.Clip.Id,
            OrderIndex = Items.IndexOf(row),
            InPointSeconds = row.InPointSeconds,
            OutPointSeconds = row.OutPointSeconds,
        }).ToList());

    private bool CanExport() => !IsExporting && Items.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (_ingestCoordinator is null)
        {
            StatusMessage = "Not connected to OBS - the export pipeline needs it to thumbnail/probe the result.";
            return;
        }

        IsExporting = true;
        StatusMessage = $"Exporting {Items.Count}-clip sequence (this re-encodes each segment, so it takes longer than a single trim)...";
        try
        {
            var segments = Items
                .Select(row => (row.Clip.FilePath, TimeSpan.FromSeconds(row.InPointSeconds), TimeSpan.FromSeconds(row.OutPointSeconds)))
                .ToList();

            var outputPath = await _sequenceExporter.ExportAsync(segments, _exportStorageFolder);

            StatusMessage = "Combined, adding to library...";
            await _ingestCoordinator.IngestAsync(outputPath, ClipSourceType.ManualRecording, isTrimmedCopy: true);

            StatusMessage = $"Exported: {Path.GetFileName(outputPath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    partial void OnIsExportingChanged(bool value) => ExportCommand.NotifyCanExecuteChanged();
}
