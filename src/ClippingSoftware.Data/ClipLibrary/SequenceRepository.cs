using ClippingSoftware.Data.Models;
using Dapper;

namespace ClippingSoftware.Data.ClipLibrary;

public class SequenceRepository(Database database)
{
    public List<SequenceClip> GetAll()
    {
        using var connection = database.OpenConnection();
        return connection.Query<SequenceClipRow>("SELECT * FROM SequenceClips ORDER BY OrderIndex")
            .Select(row => row.ToModel())
            .ToList();
    }

    public void Add(SequenceClip clip)
    {
        using var connection = database.OpenConnection();
        connection.Execute("""
            INSERT INTO SequenceClips (Id, ClipId, OrderIndex, InPointSeconds, OutPointSeconds)
            VALUES (@Id, @ClipId, @OrderIndex, @InPointSeconds, @OutPointSeconds)
            """, SequenceClipRow.FromModel(clip));
    }

    public void Remove(Guid id)
    {
        using var connection = database.OpenConnection();
        connection.Execute("DELETE FROM SequenceClips WHERE Id = @Id", new { Id = id.ToString() });
    }

    public void UpdateTrim(Guid id, double inPointSeconds, double outPointSeconds)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            "UPDATE SequenceClips SET InPointSeconds = @InPointSeconds, OutPointSeconds = @OutPointSeconds WHERE Id = @Id",
            new { Id = id.ToString(), InPointSeconds = inPointSeconds, OutPointSeconds = outPointSeconds });
    }

    /// <summary>Rewrites OrderIndex for every row in the given (already-reordered) sequence - called after
    /// a move-up/move-down so persisted order always matches what's on screen.</summary>
    public void UpdateOrder(IReadOnlyList<SequenceClip> clipsInOrder)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        for (var i = 0; i < clipsInOrder.Count; i++)
        {
            connection.Execute("UPDATE SequenceClips SET OrderIndex = @OrderIndex WHERE Id = @Id",
                new { Id = clipsInOrder[i].Id.ToString(), OrderIndex = i }, transaction);
        }
        transaction.Commit();
    }

    public void Clear()
    {
        using var connection = database.OpenConnection();
        connection.Execute("DELETE FROM SequenceClips");
    }

    private class SequenceClipRow
    {
        public string Id { get; set; } = string.Empty;
        public string ClipId { get; set; } = string.Empty;
        public int OrderIndex { get; set; }
        public double InPointSeconds { get; set; }
        public double OutPointSeconds { get; set; }

        public static SequenceClipRow FromModel(SequenceClip clip) => new()
        {
            Id = clip.Id.ToString(),
            ClipId = clip.ClipId.ToString(),
            OrderIndex = clip.OrderIndex,
            InPointSeconds = clip.InPointSeconds,
            OutPointSeconds = clip.OutPointSeconds,
        };

        public SequenceClip ToModel() => new()
        {
            Id = Guid.Parse(Id),
            ClipId = Guid.Parse(ClipId),
            OrderIndex = OrderIndex,
            InPointSeconds = InPointSeconds,
            OutPointSeconds = OutPointSeconds,
        };
    }
}
