using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClippingSoftware.Core.GameDetection;

public class KnownGameEntry
{
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("executables")]
    public List<string> Executables { get; set; } = [];
}

/// <summary>
/// Loads the curated executable -> friendly-name mapping from assets/GameDatabase/known-games.json and
/// exposes a case-insensitive, filename-only lookup.
/// </summary>
public class GameDatabase
{
    private readonly Dictionary<string, KnownGameEntry> _byExecutable = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<KnownGameEntry> Entries { get; }

    public string SourcePath { get; }

    public GameDatabase(string? jsonPath = null)
    {
        SourcePath = jsonPath ?? LocateKnownGamesFile();
        Entries = LoadEntries(SourcePath);

        foreach (var entry in Entries)
        {
            foreach (var exe in entry.Executables)
            {
                _byExecutable[exe] = entry;
            }
        }
    }

    /// <summary>
    /// Looks up a known game by executable name or full path. Matches on file name only (case-insensitive).
    /// Returns null for anything not in the curated database.
    /// </summary>
    public KnownGameEntry? FindByExecutable(string? executableNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(executableNameOrPath))
        {
            return null;
        }

        var fileName = Path.GetFileName(executableNameOrPath);
        return _byExecutable.TryGetValue(fileName, out var entry) ? entry : null;
    }

    private static List<KnownGameEntry> LoadEntries(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<KnownGameEntry>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? [];
    }

    /// <summary>
    /// Walks up from the app's base directory looking for assets\GameDatabase\known-games.json (handles
    /// running from a bin\Debug\net8.0-windows\ output folder several levels below the repo root), falling
    /// back to the known absolute repo path if that search comes up empty (e.g. a published/self-contained
    /// deployment where the assets folder wasn't copied next to the exe).
    /// </summary>
    public static string LocateKnownGamesFile()
    {
        const string relative = "assets\\GameDatabase\\known-games.json";

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return @"D:\claude stuff\clipping software\assets\GameDatabase\known-games.json";
    }
}
