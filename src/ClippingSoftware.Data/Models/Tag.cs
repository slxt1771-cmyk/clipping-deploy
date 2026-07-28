namespace ClippingSoftware.Data.Models;

/// <summary>A user-visible label a clip can be tagged with (manually, or auto-applied at ingest time from
/// the detected game/app - see TagRepository.GetOrCreateByName and RecordingIngestCoordinator). ColorHex is
/// a free "#RRGGBB" the user can repaint per tag, independent of the app's own 4-color theme.</summary>
public class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#8A8A90";
}
