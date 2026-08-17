using ClippingSoftware.Data.Models;
using Dapper;

namespace ClippingSoftware.Data.ClipLibrary;

public class SettingsRepository(Database database)
{
    public AppSettings Load()
    {
        using var connection = database.OpenConnection();
        var row = connection.QuerySingleOrDefault<AppSettingsRow>(
            "SELECT * FROM AppSettings WHERE Id = 1");

        if (row is null)
        {
            var defaults = new AppSettings();
            Save(defaults);
            return defaults;
        }

        return row.ToModel();
    }

    public void Save(AppSettings settings)
    {
        using var connection = database.OpenConnection();
        connection.Execute("""
            INSERT INTO AppSettings (
                Id, ObsExecutablePath, ObsWebsocketHost, ObsWebsocketPort, ObsWebsocketPassword,
                ClipStorageFolder, ExportStorageFolder, DefaultGameProfileId,
                ReplayBufferSaveHotkeyVk, ReplayBufferSaveHotkeyModifiers,
                StartStopRecordingHotkeyVk, StartStopRecordingHotkeyModifiers, ReplayBufferLengthSeconds,
                PlayClipSavedSound, PlayRecordingStartedSound, SoundCueVolumePercent,
                PrimaryAccentHex, SecondaryAccentHex,
                PrimaryColorHex, SecondaryColorHex, TertiaryColorHex, QuaternaryColorHex,
                HasCompletedFirstRunSetup, AutoLaunchObs
            ) VALUES (
                1, @ObsExecutablePath, @ObsWebsocketHost, @ObsWebsocketPort, @ObsWebsocketPassword,
                @ClipStorageFolder, @ExportStorageFolder, @DefaultGameProfileId,
                @ReplayBufferSaveHotkeyVk, @ReplayBufferSaveHotkeyModifiers,
                @StartStopRecordingHotkeyVk, @StartStopRecordingHotkeyModifiers, @ReplayBufferLengthSeconds,
                @PlayClipSavedSound, @PlayRecordingStartedSound, @SoundCueVolumePercent,
                @PrimaryAccentHex, @SecondaryAccentHex,
                @PrimaryColorHex, @SecondaryColorHex, @TertiaryColorHex, @QuaternaryColorHex,
                @HasCompletedFirstRunSetup, @AutoLaunchObs
            )
            ON CONFLICT(Id) DO UPDATE SET
                ObsExecutablePath = excluded.ObsExecutablePath,
                ObsWebsocketHost = excluded.ObsWebsocketHost,
                ObsWebsocketPort = excluded.ObsWebsocketPort,
                ObsWebsocketPassword = excluded.ObsWebsocketPassword,
                ClipStorageFolder = excluded.ClipStorageFolder,
                ExportStorageFolder = excluded.ExportStorageFolder,
                DefaultGameProfileId = excluded.DefaultGameProfileId,
                ReplayBufferSaveHotkeyVk = excluded.ReplayBufferSaveHotkeyVk,
                ReplayBufferSaveHotkeyModifiers = excluded.ReplayBufferSaveHotkeyModifiers,
                StartStopRecordingHotkeyVk = excluded.StartStopRecordingHotkeyVk,
                StartStopRecordingHotkeyModifiers = excluded.StartStopRecordingHotkeyModifiers,
                ReplayBufferLengthSeconds = excluded.ReplayBufferLengthSeconds,
                PlayClipSavedSound = excluded.PlayClipSavedSound,
                PlayRecordingStartedSound = excluded.PlayRecordingStartedSound,
                SoundCueVolumePercent = excluded.SoundCueVolumePercent,
                PrimaryAccentHex = excluded.PrimaryAccentHex,
                SecondaryAccentHex = excluded.SecondaryAccentHex,
                PrimaryColorHex = excluded.PrimaryColorHex,
                SecondaryColorHex = excluded.SecondaryColorHex,
                TertiaryColorHex = excluded.TertiaryColorHex,
                QuaternaryColorHex = excluded.QuaternaryColorHex,
                HasCompletedFirstRunSetup = excluded.HasCompletedFirstRunSetup,
                AutoLaunchObs = excluded.AutoLaunchObs
            """, settings);
    }

    private class AppSettingsRow
    {
        public string ObsExecutablePath { get; set; } = string.Empty;
        public string ObsWebsocketHost { get; set; } = string.Empty;
        public int ObsWebsocketPort { get; set; }
        public string ObsWebsocketPassword { get; set; } = string.Empty;
        public string ClipStorageFolder { get; set; } = string.Empty;
        public string ExportStorageFolder { get; set; } = string.Empty;
        public string? DefaultGameProfileId { get; set; }
        public int ReplayBufferSaveHotkeyVk { get; set; }
        public int ReplayBufferSaveHotkeyModifiers { get; set; }
        public int StartStopRecordingHotkeyVk { get; set; }
        public int StartStopRecordingHotkeyModifiers { get; set; }
        public int ReplayBufferLengthSeconds { get; set; }
        public bool PlayClipSavedSound { get; set; }
        public bool PlayRecordingStartedSound { get; set; }
        public int SoundCueVolumePercent { get; set; }
        public string? PrimaryAccentHex { get; set; }
        public string? SecondaryAccentHex { get; set; }
        public string? PrimaryColorHex { get; set; }
        public string? SecondaryColorHex { get; set; }
        public string? TertiaryColorHex { get; set; }
        public string? QuaternaryColorHex { get; set; }
        public bool HasCompletedFirstRunSetup { get; set; }
        public bool AutoLaunchObs { get; set; }

        public AppSettings ToModel() => new()
        {
            ObsExecutablePath = ObsExecutablePath,
            ObsWebsocketHost = ObsWebsocketHost,
            ObsWebsocketPort = ObsWebsocketPort,
            ObsWebsocketPassword = ObsWebsocketPassword,
            ClipStorageFolder = ClipStorageFolder,
            ExportStorageFolder = ExportStorageFolder,
            DefaultGameProfileId = DefaultGameProfileId,
            ReplayBufferSaveHotkeyVk = ReplayBufferSaveHotkeyVk,
            ReplayBufferSaveHotkeyModifiers = ReplayBufferSaveHotkeyModifiers,
            StartStopRecordingHotkeyVk = StartStopRecordingHotkeyVk,
            StartStopRecordingHotkeyModifiers = StartStopRecordingHotkeyModifiers,
            ReplayBufferLengthSeconds = ReplayBufferLengthSeconds,
            PlayClipSavedSound = PlayClipSavedSound,
            PlayRecordingStartedSound = PlayRecordingStartedSound,
            SoundCueVolumePercent = SoundCueVolumePercent,
            PrimaryAccentHex = PrimaryAccentHex,
            SecondaryAccentHex = SecondaryAccentHex,
            PrimaryColorHex = PrimaryColorHex,
            SecondaryColorHex = SecondaryColorHex,
            TertiaryColorHex = TertiaryColorHex,
            QuaternaryColorHex = QuaternaryColorHex,
            HasCompletedFirstRunSetup = HasCompletedFirstRunSetup,
            AutoLaunchObs = AutoLaunchObs,
        };
    }
}
