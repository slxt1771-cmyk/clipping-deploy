using System.Text.Json;
using ClippingSoftware.Data.Models;
using Dapper;

namespace ClippingSoftware.Data.ClipLibrary;

/// <summary>
/// Dapper CRUD for GameProfiles, mirroring SettingsRepository's raw-SQL/named-param pattern.
/// ExecutableMatches and Encoder are stored as JSON text columns since SQLite has no native list/object type.
/// </summary>
public class GameProfileRepository
{
    private readonly Database _database;

    public GameProfileRepository(Database database)
    {
        _database = database;
        EnsureDefaultProfileSeeded();
    }

    public List<GameProfile> GetAll()
    {
        using var connection = _database.OpenConnection();
        return connection.Query<GameProfileRow>("SELECT * FROM GameProfiles")
            .Select(row => row.ToModel())
            .ToList();
    }

    public GameProfile? GetById(Guid id)
    {
        using var connection = _database.OpenConnection();
        var row = connection.QuerySingleOrDefault<GameProfileRow>(
            "SELECT * FROM GameProfiles WHERE Id = @Id", new { Id = id.ToString() });
        return row?.ToModel();
    }

    /// <summary>
    /// The single built-in profile with IsBuiltInDefault = true. Seeded automatically on first use if the
    /// table is empty.
    /// </summary>
    public GameProfile GetDefault()
    {
        using var connection = _database.OpenConnection();
        var row = connection.QuerySingleOrDefault<GameProfileRow>(
            "SELECT * FROM GameProfiles WHERE IsBuiltInDefault = 1 LIMIT 1");

        if (row is not null)
        {
            return row.ToModel();
        }

        // Defensive: shouldn't happen after EnsureDefaultProfileSeeded, but never leave the caller without one.
        var defaultProfile = BuildDefaultProfile();
        Insert(defaultProfile);
        return defaultProfile;
    }

    public void Insert(GameProfile profile)
    {
        using var connection = _database.OpenConnection();
        connection.Execute("""
            INSERT INTO GameProfiles (
                Id, DisplayName, ExecutableMatches, RequireFullscreenOrBorderless, CaptureMode,
                CaptureTargetMonitorId, CaptureTargetWindow,
                MicEnabled, DesktopAudioEnabled, OutputWidth, OutputHeight, Fps, ObsProfileName,
                EncoderJson, IsBuiltInDefault
            ) VALUES (
                @Id, @DisplayName, @ExecutableMatches, @RequireFullscreenOrBorderless, @CaptureMode,
                @CaptureTargetMonitorId, @CaptureTargetWindow,
                @MicEnabled, @DesktopAudioEnabled, @OutputWidth, @OutputHeight, @Fps, @ObsProfileName,
                @EncoderJson, @IsBuiltInDefault
            )
            """, GameProfileRow.FromModel(profile));
    }

    public void Update(GameProfile profile)
    {
        using var connection = _database.OpenConnection();
        connection.Execute("""
            UPDATE GameProfiles SET
                DisplayName = @DisplayName,
                ExecutableMatches = @ExecutableMatches,
                RequireFullscreenOrBorderless = @RequireFullscreenOrBorderless,
                CaptureMode = @CaptureMode,
                CaptureTargetMonitorId = @CaptureTargetMonitorId,
                CaptureTargetWindow = @CaptureTargetWindow,
                MicEnabled = @MicEnabled,
                DesktopAudioEnabled = @DesktopAudioEnabled,
                OutputWidth = @OutputWidth,
                OutputHeight = @OutputHeight,
                Fps = @Fps,
                ObsProfileName = @ObsProfileName,
                EncoderJson = @EncoderJson,
                IsBuiltInDefault = @IsBuiltInDefault
            WHERE Id = @Id
            """, GameProfileRow.FromModel(profile));
    }

    public void Delete(Guid id)
    {
        using var connection = _database.OpenConnection();
        connection.Execute("DELETE FROM GameProfiles WHERE Id = @Id AND IsBuiltInDefault = 0",
            new { Id = id.ToString() });
    }

    private void EnsureDefaultProfileSeeded()
    {
        using var connection = _database.OpenConnection();
        var count = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM GameProfiles");
        if (count == 0)
        {
            Insert(BuildDefaultProfile());
        }
    }

    /// <summary>
    /// Matches the currently-provisioned "Untitled" OBS profile's live values
    /// (recordEncoder.json / basic.ini as of this milestone): 2560x1440 @ 60fps, HEVC CQP14 p6/hq/qres, 2s keyint.
    /// </summary>
    private static GameProfile BuildDefaultProfile() => new()
    {
        DisplayName = "Default",
        ExecutableMatches = [],
        RequireFullscreenOrBorderless = false,
        CaptureMode = "DisplayCapture",
        MicEnabled = true,
        DesktopAudioEnabled = true,
        OutputWidth = 2560,
        OutputHeight = 1440,
        Fps = 60,
        ObsProfileName = "Untitled",
        Encoder = new NvencSettings
        {
            Codec = "hevc",
            RateControl = "CQP",
            CqLevel = 14,
            Preset = "p6",
            Tuning = "hq",
            Multipass = "qres",
            KeyframeIntervalSec = 2,
        },
        IsBuiltInDefault = true,
    };

    private class GameProfileRow
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ExecutableMatches { get; set; } = "[]";
        public bool RequireFullscreenOrBorderless { get; set; }
        public string CaptureMode { get; set; } = string.Empty;
        public string? CaptureTargetMonitorId { get; set; }
        public string? CaptureTargetWindow { get; set; }
        public bool MicEnabled { get; set; }
        public bool DesktopAudioEnabled { get; set; }
        public int OutputWidth { get; set; }
        public int OutputHeight { get; set; }
        public int Fps { get; set; }
        public string ObsProfileName { get; set; } = string.Empty;
        public string EncoderJson { get; set; } = "{}";
        public bool IsBuiltInDefault { get; set; }

        public static GameProfileRow FromModel(GameProfile profile) => new()
        {
            Id = profile.Id.ToString(),
            DisplayName = profile.DisplayName,
            ExecutableMatches = JsonSerializer.Serialize(profile.ExecutableMatches),
            RequireFullscreenOrBorderless = profile.RequireFullscreenOrBorderless,
            CaptureMode = profile.CaptureMode,
            CaptureTargetMonitorId = profile.CaptureTargetMonitorId,
            CaptureTargetWindow = profile.CaptureTargetWindow,
            MicEnabled = profile.MicEnabled,
            DesktopAudioEnabled = profile.DesktopAudioEnabled,
            OutputWidth = profile.OutputWidth,
            OutputHeight = profile.OutputHeight,
            Fps = profile.Fps,
            ObsProfileName = profile.ObsProfileName,
            EncoderJson = JsonSerializer.Serialize(profile.Encoder),
            IsBuiltInDefault = profile.IsBuiltInDefault,
        };

        public GameProfile ToModel() => new()
        {
            Id = Guid.Parse(Id),
            DisplayName = DisplayName,
            ExecutableMatches = JsonSerializer.Deserialize<List<string>>(ExecutableMatches) ?? [],
            RequireFullscreenOrBorderless = RequireFullscreenOrBorderless,
            CaptureMode = CaptureMode,
            CaptureTargetMonitorId = CaptureTargetMonitorId,
            CaptureTargetWindow = CaptureTargetWindow,
            MicEnabled = MicEnabled,
            DesktopAudioEnabled = DesktopAudioEnabled,
            OutputWidth = OutputWidth,
            OutputHeight = OutputHeight,
            Fps = Fps,
            ObsProfileName = ObsProfileName,
            Encoder = JsonSerializer.Deserialize<NvencSettings>(EncoderJson) ?? new NvencSettings(),
            IsBuiltInDefault = IsBuiltInDefault,
        };
    }
}
