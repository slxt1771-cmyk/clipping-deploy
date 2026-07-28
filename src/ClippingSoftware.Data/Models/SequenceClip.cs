namespace ClippingSoftware.Data.Models;

/// <summary>
/// One clip placed into the persistent "scratch" sequence (M16's simple multi-clip editor - pick several
/// clips, trim each, export as one combined file). There is exactly one sequence at a time by design (a
/// simple alternative editing spot, not a project manager with multiple named/saved sequences); rows
/// persist immediately on every add/remove/reorder/trim change, so an app restart never loses the
/// in-progress sequence.
/// </summary>
public class SequenceClip
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClipId { get; set; }
    public int OrderIndex { get; set; }
    public double InPointSeconds { get; set; }
    public double OutPointSeconds { get; set; }
}
