using System.Text.Json;
using ClippingSoftware.Data.Models;
using Dapper;

namespace ClippingSoftware.Data.ClipLibrary;

public class ClipRepository(Database database)
{
    public void Insert(ClipMetadata clip)
    {
        using var connection = database.OpenConnection();
        connection.Execute("""
            INSERT INTO Clips (
                Id, FilePath, ThumbnailPath, RecordedAtUtc, DurationTicks,
                DetectedGameProfileId, DetectedGameDisplayName, Source,
                VideoWidth, VideoHeight, AudioTracksJson,
                IsTrimmedCopy, SourceClipId
            ) VALUES (
                @Id, @FilePath, @ThumbnailPath, @RecordedAtUtc, @DurationTicks,
                @DetectedGameProfileId, @DetectedGameDisplayName, @Source,
                @VideoWidth, @VideoHeight, @AudioTracksJson,
                @IsTrimmedCopy, @SourceClipId
            )
            """, ClipRow.FromModel(clip));
    }

    public List<ClipMetadata> GetAll()
    {
        using var connection = database.OpenConnection();
        return connection.Query<ClipRow>("SELECT * FROM Clips ORDER BY RecordedAtUtc DESC")
            .Select(row => row.ToModel())
            .ToList();
    }

    public ClipMetadata? GetById(Guid id)
    {
        using var connection = database.OpenConnection();
        var row = connection.QuerySingleOrDefault<ClipRow>(
            "SELECT * FROM Clips WHERE Id = @Id", new { Id = id.ToString() });
        return row?.ToModel();
    }

    public void Delete(Guid id)
    {
        using var connection = database.OpenConnection();
        connection.Execute("DELETE FROM Clips WHERE Id = @Id", new { Id = id.ToString() });
    }

    private class ClipRow
    {
        public string Id { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string? ThumbnailPath { get; set; }
        public DateTime RecordedAtUtc { get; set; }
        public long DurationTicks { get; set; }
        public string? DetectedGameProfileId { get; set; }
        public string? DetectedGameDisplayName { get; set; }
        public int Source { get; set; }
        public int VideoWidth { get; set; }
        public int VideoHeight { get; set; }
        public string AudioTracksJson { get; set; } = "[]";
        public bool IsTrimmedCopy { get; set; }
        public string? SourceClipId { get; set; }

        public static ClipRow FromModel(ClipMetadata clip) => new()
        {
            Id = clip.Id.ToString(),
            FilePath = clip.FilePath,
            ThumbnailPath = clip.ThumbnailPath,
            RecordedAtUtc = clip.RecordedAtUtc,
            DurationTicks = clip.Duration.Ticks,
            DetectedGameProfileId = clip.DetectedGameProfileId?.ToString(),
            DetectedGameDisplayName = clip.DetectedGameDisplayName,
            Source = (int)clip.Source,
            VideoWidth = clip.VideoWidth,
            VideoHeight = clip.VideoHeight,
            AudioTracksJson = JsonSerializer.Serialize(clip.AudioTracks),
            IsTrimmedCopy = clip.IsTrimmedCopy,
            SourceClipId = clip.SourceClipId?.ToString(),
        };

        public ClipMetadata ToModel() => new()
        {
            Id = Guid.Parse(Id),
            FilePath = FilePath,
            ThumbnailPath = ThumbnailPath,
            RecordedAtUtc = RecordedAtUtc,
            Duration = TimeSpan.FromTicks(DurationTicks),
            DetectedGameProfileId = DetectedGameProfileId is null ? null : Guid.Parse(DetectedGameProfileId),
            DetectedGameDisplayName = DetectedGameDisplayName,
            Source = (ClipSourceType)Source,
            VideoWidth = VideoWidth,
            VideoHeight = VideoHeight,
            AudioTracks = JsonSerializer.Deserialize<List<AudioTrackInfo>>(AudioTracksJson) ?? [],
            IsTrimmedCopy = IsTrimmedCopy,
            SourceClipId = SourceClipId is null ? null : Guid.Parse(SourceClipId),
        };
    }
}
