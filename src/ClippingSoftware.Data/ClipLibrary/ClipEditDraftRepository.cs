using System.Text.Json;
using ClippingSoftware.Data.Models;
using Dapper;

namespace ClippingSoftware.Data.ClipLibrary;

public class ClipEditDraftRepository(Database database)
{
    public ClipEditDraft? Find(Guid clipId)
    {
        using var connection = database.OpenConnection();
        return connection.QuerySingleOrDefault<ClipEditDraftRow>(
            "SELECT * FROM ClipEditDrafts WHERE ClipId = @ClipId", new { ClipId = clipId.ToString() })?.ToModel();
    }

    /// <summary>Upsert - called on every edit (in/out points, frame-accurate toggle, per-track
    /// enable/volume), not on a debounce, so progress is never lost to an unexpected restart.</summary>
    public void Save(ClipEditDraft draft)
    {
        using var connection = database.OpenConnection();
        connection.Execute("""
            INSERT INTO ClipEditDrafts (ClipId, InPointSeconds, OutPointSeconds, IsFrameAccurate, AudioTracksJson)
            VALUES (@ClipId, @InPointSeconds, @OutPointSeconds, @IsFrameAccurate, @AudioTracksJson)
            ON CONFLICT(ClipId) DO UPDATE SET
                InPointSeconds = excluded.InPointSeconds,
                OutPointSeconds = excluded.OutPointSeconds,
                IsFrameAccurate = excluded.IsFrameAccurate,
                AudioTracksJson = excluded.AudioTracksJson
            """, ClipEditDraftRow.FromModel(draft));
    }

    /// <summary>Called once the user actually exports - the draft's job (preserving in-progress work) is
    /// done at that point, so it's cleared rather than left to linger.</summary>
    public void Delete(Guid clipId)
    {
        using var connection = database.OpenConnection();
        connection.Execute("DELETE FROM ClipEditDrafts WHERE ClipId = @ClipId", new { ClipId = clipId.ToString() });
    }

    private class ClipEditDraftRow
    {
        public string ClipId { get; set; } = string.Empty;
        public double InPointSeconds { get; set; }
        public double OutPointSeconds { get; set; }
        public bool IsFrameAccurate { get; set; }
        public string AudioTracksJson { get; set; } = "[]";

        public static ClipEditDraftRow FromModel(ClipEditDraft draft) => new()
        {
            ClipId = draft.ClipId.ToString(),
            InPointSeconds = draft.InPointSeconds,
            OutPointSeconds = draft.OutPointSeconds,
            IsFrameAccurate = draft.IsFrameAccurate,
            AudioTracksJson = JsonSerializer.Serialize(draft.AudioTracks),
        };

        public ClipEditDraft ToModel() => new()
        {
            ClipId = Guid.Parse(ClipId),
            InPointSeconds = InPointSeconds,
            OutPointSeconds = OutPointSeconds,
            IsFrameAccurate = IsFrameAccurate,
            AudioTracks = JsonSerializer.Deserialize<List<DraftAudioTrackState>>(AudioTracksJson) ?? [],
        };
    }
}
