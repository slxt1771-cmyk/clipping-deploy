using System.Collections.ObjectModel;
using System.IO;
using ClippingSoftware.Data.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClippingSoftware.App.ViewModels;

/// <summary>
/// Mostly-read-only binding-friendly wrapper around a <see cref="ClipMetadata"/> row for display in the
/// clip browser grid. Formats raw metadata (duration, timestamp, source type, game name) into strings
/// suited for XAML binding without cluttering the data model itself. The one mutable piece is Tags - an
/// ObservableCollection so the card's tag chips update live when TagManagerViewModel reassigns tags,
/// without needing this whole class to be a full ObservableObject.
/// </summary>
public partial class ClipItemViewModel : ObservableObject
{
    public ClipItemViewModel(ClipMetadata clip)
    {
        Clip = clip;
    }

    /// <summary>The underlying persisted row. Exposed so commands (delete, open, trim) can act on it.</summary>
    public ClipMetadata Clip { get; }

    /// <summary>Tags currently assigned to this clip (manual + auto-tagged-by-game). Populated by
    /// ClipBrowserViewModel after load from TagRepository.GetTagsByClip(), and refreshed wholesale
    /// (clear + re-add) whenever TagManagerViewModel.TagsChanged fires - see ClipBrowserViewModel.</summary>
    public ObservableCollection<Tag> Tags { get; } = [];

    public Guid Id => Clip.Id;

    public string FilePath => Clip.FilePath;

    /// <summary>Uppercased file name (no path) for the card title, e.g. "2026-07-18_220145.MP4" -
    /// matches the module-card title convention without inventing a name the clip doesn't have.</summary>
    public string FileNameTag => Path.GetFileName(Clip.FilePath).ToUpperInvariant();

    /// <summary>Null/missing means no thumbnail was generated yet or the file was removed; the view shows a placeholder.</summary>
    public string? ThumbnailPath => Clip.ThumbnailPath;

    public string DurationText => FormatDuration(Clip.Duration);

    /// <summary>Absolute timestamp for a tooltip/secondary label.</summary>
    public string RecordedAtText => Clip.RecordedAtUtc.ToLocalTime().ToString("g");

    /// <summary>Relative "X ago" label for the primary display, e.g. "3m ago", "2h ago", "5d ago".</summary>
    public string RecordedAgoText => FormatRelativeTime(Clip.RecordedAtUtc);

    public string GameDisplayName => string.IsNullOrWhiteSpace(Clip.DetectedGameDisplayName)
        ? "Unknown"
        : Clip.DetectedGameDisplayName;

    public string SourceLabel => Clip.Source switch
    {
        ClipSourceType.ReplayBuffer => "Replay",
        ClipSourceType.ManualRecording => "Manual",
        _ => Clip.Source.ToString(),
    };

    public string ResolutionText => Clip.VideoWidth > 0 && Clip.VideoHeight > 0
        ? $"{Clip.VideoWidth}x{Clip.VideoHeight}"
        : string.Empty;

    /// <summary>Friendly resolution tier ("4K"/"1440P"/"1080P"/"720P"), falling back to the raw
    /// WxH for anything outside those common tiers. Paired with SourceLabel for the card's tag
    /// chip - real metadata only, no fabricated FPS (the app doesn't record per-clip frame rate).</summary>
    public string ResolutionTag => Clip.VideoWidth switch
    {
        >= 3840 => "4K",
        >= 2560 => "1440P",
        >= 1920 => "1080P",
        >= 1280 => "720P",
        > 0 => ResolutionText,
        _ => "UNKNOWN",
    };

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
    }

    private static string FormatRelativeTime(DateTime recordedAtUtc)
    {
        var elapsed = DateTime.UtcNow - recordedAtUtc;

        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return elapsed switch
        {
            { TotalSeconds: < 60 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)elapsed.TotalMinutes}m ago",
            { TotalHours: < 24 } => $"{(int)elapsed.TotalHours}h ago",
            { TotalDays: < 7 } => $"{(int)elapsed.TotalDays}d ago",
            _ => recordedAtUtc.ToLocalTime().ToString("yyyy-MM-dd"),
        };
    }
}
