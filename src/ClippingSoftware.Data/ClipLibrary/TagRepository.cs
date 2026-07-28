using ClippingSoftware.Data.Models;
using Dapper;

namespace ClippingSoftware.Data.ClipLibrary;

/// <summary>Persists Tags and their many-to-many assignment to Clips (ClipTags). Used both by the App
/// layer's tag manager UI (manual create/recolor/rename/delete/assign) and by RecordingIngestCoordinator
/// at ingest time (auto-tag a clip by its detected game/app - see GetOrCreateByName).</summary>
public class TagRepository(Database database)
{
    public List<Tag> GetAll()
    {
        using var connection = database.OpenConnection();
        return connection.Query<Tag>("SELECT Id, Name, ColorHex FROM Tags ORDER BY Name")
            .ToList();
    }

    public Tag Add(string name, string colorHex)
    {
        var tag = new Tag { Name = name, ColorHex = colorHex };
        using var connection = database.OpenConnection();
        connection.Execute(
            "INSERT INTO Tags (Id, Name, ColorHex) VALUES (@Id, @Name, @ColorHex)",
            new { Id = tag.Id.ToString(), tag.Name, tag.ColorHex });
        return tag;
    }

    public void Update(Tag tag)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            "UPDATE Tags SET Name = @Name, ColorHex = @ColorHex WHERE Id = @Id",
            new { Id = tag.Id.ToString(), tag.Name, tag.ColorHex });
    }

    public void Delete(Guid id)
    {
        using var connection = database.OpenConnection();
        connection.Execute("DELETE FROM ClipTags WHERE TagId = @Id", new { Id = id.ToString() });
        connection.Execute("DELETE FROM Tags WHERE Id = @Id", new { Id = id.ToString() });
    }

    /// <summary>Case-insensitive lookup-or-create, keyed on Name - used to auto-tag a clip by its detected
    /// game/app display name at ingest time without creating a duplicate "Rust"/"rust" pair if the game was
    /// already tagged manually or from an earlier clip.</summary>
    public Tag GetOrCreateByName(string name, string defaultColorHex)
    {
        using var connection = database.OpenConnection();
        var existing = connection.QuerySingleOrDefault<Tag>(
            "SELECT Id, Name, ColorHex FROM Tags WHERE Name = @Name COLLATE NOCASE", new { Name = name });
        if (existing is not null)
        {
            return existing;
        }

        var tag = new Tag { Name = name, ColorHex = defaultColorHex };
        connection.Execute(
            "INSERT INTO Tags (Id, Name, ColorHex) VALUES (@Id, @Name, @ColorHex)",
            new { Id = tag.Id.ToString(), tag.Name, tag.ColorHex });
        return tag;
    }

    /// <summary>All ClipId -> Tag links in one query, grouped for the library grid to attach to each
    /// ClipItemViewModel without an N+1 query per clip.</summary>
    public Dictionary<Guid, List<Tag>> GetTagsByClip()
    {
        using var connection = database.OpenConnection();
        var rows = connection.Query<ClipTagRow>("""
            SELECT ct.ClipId, t.Id, t.Name, t.ColorHex
            FROM ClipTags ct
            JOIN Tags t ON t.Id = ct.TagId
            """);

        var result = new Dictionary<Guid, List<Tag>>();
        foreach (var row in rows)
        {
            var clipId = Guid.Parse(row.ClipId);
            if (!result.TryGetValue(clipId, out var list))
            {
                list = [];
                result[clipId] = list;
            }
            list.Add(new Tag { Id = Guid.Parse(row.Id), Name = row.Name, ColorHex = row.ColorHex });
        }
        return result;
    }

    public void AddClipTag(Guid clipId, Guid tagId)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            "INSERT OR IGNORE INTO ClipTags (ClipId, TagId) VALUES (@ClipId, @TagId)",
            new { ClipId = clipId.ToString(), TagId = tagId.ToString() });
    }

    /// <summary>Removes every tag link for a clip - called when the clip itself is deleted, so ClipTags
    /// doesn't accumulate rows pointing at a Clips id that no longer exists.</summary>
    public void RemoveAllForClip(Guid clipId)
    {
        using var connection = database.OpenConnection();
        connection.Execute("DELETE FROM ClipTags WHERE ClipId = @ClipId", new { ClipId = clipId.ToString() });
    }

    public void RemoveClipTag(Guid clipId, Guid tagId)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            "DELETE FROM ClipTags WHERE ClipId = @ClipId AND TagId = @TagId",
            new { ClipId = clipId.ToString(), TagId = tagId.ToString() });
    }

    private class ClipTagRow
    {
        public string ClipId { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ColorHex { get; set; } = string.Empty;
    }
}
