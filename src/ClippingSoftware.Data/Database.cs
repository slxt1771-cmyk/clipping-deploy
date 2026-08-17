using Microsoft.Data.Sqlite;

namespace ClippingSoftware.Data;

public class Database
{
    private readonly string _connectionString;

    public Database(string? dbPath = null)
    {
        dbPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClippingSoftware", "app.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        CreateTables(connection);

        // Additive migrations for installs that predate a given column - CREATE TABLE IF NOT EXISTS only
        // helps fresh installs; an existing table needs its own columns added in place so this machine's
        // real settings/profiles/clip library aren't lost by touching schema (see PROJECT.md: prefer
        // additive changes over destructive ones where easy).
        AddColumnIfMissing(connection, "GameProfiles", "CaptureTargetMonitorId", "TEXT NULL");
        AddColumnIfMissing(connection, "GameProfiles", "CaptureTargetWindow", "TEXT NULL");
        AddColumnIfMissing(connection, "AppSettings", "PlayClipSavedSound", "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfMissing(connection, "AppSettings", "PlayRecordingStartedSound", "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfMissing(connection, "AppSettings", "SoundCueVolumePercent", "INTEGER NOT NULL DEFAULT 70");
        AddColumnIfMissing(connection, "AppSettings", "PrimaryAccentHex", "TEXT NULL");
        AddColumnIfMissing(connection, "AppSettings", "SecondaryAccentHex", "TEXT NULL");
        // 4-color editable theme (background / panel / text+lines+signal / alert-destructive) superseding
        // the old 2-color "accent only" scheme above - PrimaryAccentHex/SecondaryAccentHex are left in
        // place unused rather than dropped (nullable, harmless, and this is a single-dev-machine install -
        // see PROJECT.md: prefer additive changes where easy).
        AddColumnIfMissing(connection, "AppSettings", "PrimaryColorHex", "TEXT NULL");
        AddColumnIfMissing(connection, "AppSettings", "SecondaryColorHex", "TEXT NULL");
        AddColumnIfMissing(connection, "AppSettings", "TertiaryColorHex", "TEXT NULL");
        AddColumnIfMissing(connection, "AppSettings", "QuaternaryColorHex", "TEXT NULL");
        AddColumnIfMissing(connection, "GameProfiles", "AutoDetectResolution", "INTEGER NOT NULL DEFAULT 0");

        if (AddColumnIfMissing(connection, "Clips", "AudioTracksJson", "TEXT NOT NULL DEFAULT '[]'"))
        {
            BackfillAudioTracksJsonFromLegacyColumns(connection);
        }

        // HasMicTrack/HasDesktopAudioTrack are fully superseded by AudioTracksJson (M11) but were only
        // ever backfilled, never dropped - ClipRepository's INSERT stopped supplying them, so on an
        // install that predates M11 every new clip ingest failed with a NOT NULL constraint violation on
        // these leftover columns. Requires SQLite 3.35+ (ALTER TABLE ... DROP COLUMN, added 2021);
        // Microsoft.Data.Sqlite's bundled SQLite is well past that.
        DropColumnIfExists(connection, "Clips", "HasMicTrack");
        DropColumnIfExists(connection, "Clips", "HasDesktopAudioTrack");
    }

    private static void CreateTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS AppSettings (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                ObsExecutablePath TEXT NOT NULL,
                ObsWebsocketHost TEXT NOT NULL,
                ObsWebsocketPort INTEGER NOT NULL,
                ObsWebsocketPassword TEXT NOT NULL,
                ClipStorageFolder TEXT NOT NULL,
                ExportStorageFolder TEXT NOT NULL,
                DefaultGameProfileId TEXT NULL,
                ReplayBufferSaveHotkeyVk INTEGER NOT NULL,
                ReplayBufferSaveHotkeyModifiers INTEGER NOT NULL,
                StartStopRecordingHotkeyVk INTEGER NOT NULL,
                StartStopRecordingHotkeyModifiers INTEGER NOT NULL,
                ReplayBufferLengthSeconds INTEGER NOT NULL,
                PlayClipSavedSound INTEGER NOT NULL DEFAULT 1,
                PlayRecordingStartedSound INTEGER NOT NULL DEFAULT 1,
                SoundCueVolumePercent INTEGER NOT NULL DEFAULT 70,
                PrimaryAccentHex TEXT NULL,
                SecondaryAccentHex TEXT NULL,
                PrimaryColorHex TEXT NULL,
                SecondaryColorHex TEXT NULL,
                TertiaryColorHex TEXT NULL,
                QuaternaryColorHex TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS GameProfiles (
                Id TEXT PRIMARY KEY,
                DisplayName TEXT NOT NULL,
                ExecutableMatches TEXT NOT NULL,
                RequireFullscreenOrBorderless INTEGER NOT NULL,
                CaptureMode TEXT NOT NULL,
                CaptureTargetMonitorId TEXT NULL,
                CaptureTargetWindow TEXT NULL,
                MicEnabled INTEGER NOT NULL,
                DesktopAudioEnabled INTEGER NOT NULL,
                OutputWidth INTEGER NOT NULL,
                OutputHeight INTEGER NOT NULL,
                AutoDetectResolution INTEGER NOT NULL DEFAULT 0,
                Fps INTEGER NOT NULL,
                ObsProfileName TEXT NOT NULL,
                EncoderJson TEXT NOT NULL,
                IsBuiltInDefault INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Clips (
                Id TEXT PRIMARY KEY,
                FilePath TEXT NOT NULL,
                ThumbnailPath TEXT NULL,
                RecordedAtUtc TEXT NOT NULL,
                DurationTicks INTEGER NOT NULL,
                DetectedGameProfileId TEXT NULL,
                DetectedGameDisplayName TEXT NULL,
                Source INTEGER NOT NULL,
                VideoWidth INTEGER NOT NULL,
                VideoHeight INTEGER NOT NULL,
                AudioTracksJson TEXT NOT NULL,
                IsTrimmedCopy INTEGER NOT NULL,
                SourceClipId TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS AudioSources (
                Id TEXT PRIMARY KEY,
                InputName TEXT NOT NULL,
                DisplayLabel TEXT NOT NULL,
                WindowTarget TEXT NOT NULL,
                TrackNumber INTEGER NOT NULL
            );

            -- M16: in-progress trim state for the embedded single-clip editor, one row per clip, written
            -- through on every change (not a debounced autosave) so a restart never loses progress.
            CREATE TABLE IF NOT EXISTS ClipEditDrafts (
                ClipId TEXT PRIMARY KEY,
                InPointSeconds REAL NOT NULL,
                OutPointSeconds REAL NOT NULL,
                IsFrameAccurate INTEGER NOT NULL,
                AudioTracksJson TEXT NOT NULL
            );

            -- M16: the one persistent "scratch" multi-clip sequence (singular by design - a simple
            -- alternative editing spot, not a project manager with multiple named sequences). Each row is
            -- one clip placed in the sequence; OrderIndex is its position.
            CREATE TABLE IF NOT EXISTS SequenceClips (
                Id TEXT PRIMARY KEY,
                ClipId TEXT NOT NULL,
                OrderIndex INTEGER NOT NULL,
                InPointSeconds REAL NOT NULL,
                OutPointSeconds REAL NOT NULL
            );

            -- User-defined and auto-applied (by detected game/app, see RecordingIngestCoordinator) clip
            -- labels. ClipTags is a plain many-to-many join - a clip can carry any number of tags, and a
            -- tag can be removed from Tags without touching Clips (ClipTags rows are just cascaded away).
            CREATE TABLE IF NOT EXISTS Tags (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                ColorHex TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ClipTags (
                ClipId TEXT NOT NULL,
                TagId TEXT NOT NULL,
                PRIMARY KEY (ClipId, TagId)
            );
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Adds a column if it isn't already present; returns true iff it was actually added (so
    /// callers can tell "fresh column, needs backfill" apart from "already there from a prior run").</summary>
    private static bool AddColumnIfMissing(SqliteConnection connection, string table, string column, string columnDefinitionSql)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = @name";
            check.Parameters.AddWithValue("@name", column);
            if (Convert.ToInt64(check.ExecuteScalar()) > 0)
            {
                return false;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDefinitionSql}";
        alter.ExecuteNonQuery();
        return true;
    }

    /// <summary>Mirror of AddColumnIfMissing for the other direction: drops a column that's no longer
    /// used, idempotently (a no-op once already dropped).</summary>
    private static bool DropColumnIfExists(SqliteConnection connection, string table, string column)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = @name";
            check.Parameters.AddWithValue("@name", column);
            if (Convert.ToInt64(check.ExecuteScalar()) == 0)
            {
                return false;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} DROP COLUMN {column}";
        alter.ExecuteNonQuery();
        return true;
    }

    /// <summary>
    /// One-time conversion for installs that recorded clips before AudioTracksJson existed: the old
    /// HasDesktopAudioTrack/HasMicTrack booleans (from ClipMetadataExtractor's original fixed Track1=desktop/
    /// Track2=mic convention) become the equivalent AudioTracksJson entries instead of just defaulting to
    /// "[]" and losing that info. A brand-new install never has these legacy columns, so this is a no-op
    /// for it (AddColumnIfMissing's '[]' DEFAULT is already correct there).
    /// </summary>
    private static void BackfillAudioTracksJsonFromLegacyColumns(SqliteConnection connection)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Clips') WHERE name = 'HasDesktopAudioTrack'";
            if (Convert.ToInt64(check.ExecuteScalar()) == 0)
            {
                return;
            }
        }

        var updates = new List<(string Id, string Json)>();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT Id, HasDesktopAudioTrack, HasMicTrack FROM Clips";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var hasDesktop = reader.GetInt64(1) != 0;
                var hasMic = reader.GetInt64(2) != 0;

                var tracks = new List<string>();
                var streamIndex = 0;
                if (hasDesktop)
                {
                    tracks.Add($$"""{"StreamIndex":{{streamIndex++}},"Label":"Desktop Audio"}""");
                }
                if (hasMic)
                {
                    tracks.Add($$"""{"StreamIndex":{{streamIndex++}},"Label":"Mic/Aux"}""");
                }
                updates.Add((reader.GetString(0), "[" + string.Join(",", tracks) + "]"));
            }
        }

        foreach (var (id, json) in updates)
        {
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE Clips SET AudioTracksJson = @Json WHERE Id = @Id";
            update.Parameters.AddWithValue("@Json", json);
            update.Parameters.AddWithValue("@Id", id);
            update.ExecuteNonQuery();
        }
    }
}
