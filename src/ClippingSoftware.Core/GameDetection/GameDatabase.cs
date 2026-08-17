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

    /// <summary>
    /// Returns an empty database rather than throwing for a missing or unreadable file. Game detection is a
    /// background convenience - an unparseable curated list should degrade to "no game is ever recognized"
    /// (everything resolves to the Default profile), not prevent the app from starting, and this type is
    /// constructed from a MainViewModel field initializer where a throw would do exactly that.
    /// </summary>
    private static List<KnownGameEntry> LoadEntries(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<KnownGameEntry>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Locates assets\GameDatabase\known-games.json beside the exe (packaged install) or at the repo root
    /// (running from a bin output folder) - see <see cref="BundledResources"/>. Returns the expected
    /// install-relative path when the file is missing entirely, so <see cref="LoadEntries"/> degrades to an
    /// empty database and <see cref="SourcePath"/> still names where the file was supposed to be.
    /// </summary>
    public static string LocateKnownGamesFile()
    {
        BundledResources.TryResolve(out var path, "assets", "GameDatabase", "known-games.json");
        return path;
    }
}
