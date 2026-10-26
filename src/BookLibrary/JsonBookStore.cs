using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookLibrary;

/// <summary>
/// The whole library is one JSON file. Saves are atomic (temp file in
/// the same directory, then rename), so a crash mid-save cannot leave
/// a half-written library. A corrupt file is quarantined with a
/// timestamp suffix instead of crashing, and the CLI reports it.
/// </summary>
public sealed class JsonBookStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Path { get; }
    public bool LastLoadWasCorrupt { get; private set; }

    public JsonBookStore(string path) => Path = path;

    public sealed record Data(List<Book> Books, ReadingGoal? Goal);

    public Data Load()
    {
        LastLoadWasCorrupt = false;
        if (!File.Exists(Path)) return new Data(new List<Book>(), null);
        try
        {
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(Path), Options);
            return data ?? new Data(new List<Book>(), null);
        }
        catch (JsonException)
        {
            var quarantine = $"{Path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(Path, quarantine);
            LastLoadWasCorrupt = true;
            return new Data(new List<Book>(), null);
        }
    }

    public void Save(Library library, ReadingGoal? goal)
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(
            new Data(library.Books.ToList(), goal), Options));
        File.Move(tmp, Path, overwrite: true);
    }
}

/// <summary>Where the data file lives; overridable for tests and scripts.</summary>
public static class DataPaths
{
    public static string Default()
    {
        var env = Environment.GetEnvironmentVariable("BOOKLIBRARY_DATA");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".booklibrary", "books.json");
    }
}
