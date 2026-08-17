using Dapper;

namespace ClippingSoftware.Data.ClipLibrary;

/// <summary>Persisted record of one app-audio source AudioSourceManager has provisioned in OBS (the two
/// built-ins, Desktop Audio/Mic, are never persisted here - they always exist and are never removed).</summary>
public record AudioSourceRecord(Guid Id, string InputName, string DisplayLabel, string WindowTarget, int TrackNumber);

public class AudioSourceRepository(Database database)
{
    public List<AudioSourceRecord> GetAll()
    {
        using var connection = database.OpenConnection();
        return connection.Query<AudioSourceRow>("SELECT * FROM AudioSources ORDER BY TrackNumber")
            .Select(row => row.ToRecord())
            .ToList();
    }

    public void Insert(AudioSourceRecord source)
    {
        using var connection = database.OpenConnection();
        connection.Execute("""
            INSERT INTO AudioSources (Id, InputName, DisplayLabel, WindowTarget, TrackNumber)
            VALUES (@Id, @InputName, @DisplayLabel, @WindowTarget, @TrackNumber)
            """, AudioSourceRow.FromRecord(source));
    }

    public void Delete(string inputName)
    {
        using var connection = database.OpenConnection();
        connection.Execute("DELETE FROM AudioSources WHERE InputName = @InputName", new { InputName = inputName });
    }

    /// <summary>Updates just the persisted window target for an existing source (AudioSourceManager's
    /// periodic re-resolution against whatever's currently running - see RefreshAppSourceTargets), so a
    /// later app restart's stale-fallback matching starts from the most recently known-good value instead
    /// of whatever the source happened to be created with, possibly a long time ago.</summary>
    public void UpdateWindowTarget(string inputName, string windowTarget)
    {
        using var connection = database.OpenConnection();
        connection.Execute("UPDATE AudioSources SET WindowTarget = @WindowTarget WHERE InputName = @InputName",
            new { WindowTarget = windowTarget, InputName = inputName });
    }

    private class AudioSourceRow
    {
        public string Id { get; set; } = string.Empty;
        public string InputName { get; set; } = string.Empty;
        public string DisplayLabel { get; set; } = string.Empty;
        public string WindowTarget { get; set; } = string.Empty;
        public int TrackNumber { get; set; }

        public static AudioSourceRow FromRecord(AudioSourceRecord source) => new()
        {
            Id = source.Id.ToString(),
            InputName = source.InputName,
            DisplayLabel = source.DisplayLabel,
            WindowTarget = source.WindowTarget,
            TrackNumber = source.TrackNumber,
        };

        public AudioSourceRecord ToRecord() => new(
            Guid.Parse(Id), InputName, DisplayLabel, WindowTarget, TrackNumber);
    }
}
