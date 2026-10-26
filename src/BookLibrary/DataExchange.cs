using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookLibrary;

/// <summary>
/// Import and export. JSON uses exactly the data-file schema, so an
/// export is a portable copy of a library and re-importing it is a
/// no-op. CSV is the spreadsheet-friendly shape: same fields, one
/// book per row, quoted fields handled properly.
/// </summary>
public static class DataExchange
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public sealed record ImportResult(int Added, int Skipped, ReadingGoal? Goal);

    public static string ToJson(Library library, ReadingGoal? goal) =>
        JsonSerializer.Serialize(new JsonBookStore.Data(library.Books.ToList(), goal), Options);

    public static ImportResult ImportJson(Library library, string text)
    {
        var data = JsonSerializer.Deserialize<JsonBookStore.Data>(text, Options)
            ?? throw new FormatException("Not a BookLibrary JSON file.");
        return Merge(library, data.Books, data.Goal);
    }

    public static ImportResult Merge(Library library, IEnumerable<Book> books, ReadingGoal? goal)
    {
        var added = 0;
        var skipped = 0;
        foreach (var book in books)
        {
            book.Validate();
            if (library.FindDuplicate(book) is not null) { skipped++; continue; }
            library.Add(book);
            added++;
        }
        return new ImportResult(added, skipped, goal);
    }

    public static string ToCsv(Library library)
    {
        var sb = new StringBuilder();
        sb.AppendLine("id,title,author,isbn,year,genre,tags,totalPages,currentPage,status,rating,startedOn,finishedOn,notes");
        foreach (var b in library.Books)
        {
            string Field(object? v) => Escape(v?.ToString() ?? "");
            sb.AppendLine(string.Join(",", new[]
            {
                Field(b.Id), Field(b.Title), Field(b.Author), Field(b.Isbn), Field(b.Year),
                Field(b.Genre), Field(string.Join(";", b.Tags)), Field(b.TotalPages),
                Field(b.CurrentPage), Field(b.Status), Field(b.Rating),
                Field(b.StartedOn?.ToString("yyyy-MM-dd")), Field(b.FinishedOn?.ToString("yyyy-MM-dd")),
                Field(b.Notes)
            }));
        }
        return sb.ToString();
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    public static ImportResult ImportCsv(Library library, string text)
    {
        var rows = ParseCsv(text);
        if (rows.Count < 2) throw new FormatException("CSV file has no data rows.");
        var header = rows[0];
        var books = new List<Book>();
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            string Get(string name)
            {
                var idx = header.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
                return idx >= 0 && idx < row.Count ? row[idx] : "";
            }
            var book = new Book
            {
                Title = Get("title"),
                Author = Get("author"),
                Isbn = string.IsNullOrWhiteSpace(Get("isbn")) ? null : Get("isbn"),
                Year = int.TryParse(Get("year"), out var y) ? y : null,
                Genre = string.IsNullOrWhiteSpace(Get("genre")) ? null : Get("genre"),
                Tags = Get("tags").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(),
                TotalPages = int.TryParse(Get("totalPages"), out var tp) ? tp : 0,
                CurrentPage = int.TryParse(Get("currentPage"), out var cp) ? cp : 0,
                Status = Enum.TryParse<ReadingStatus>(Get("status"), ignoreCase: true, out var st) ? st : ReadingStatus.WantToRead,
                Rating = int.TryParse(Get("rating"), out var r) ? r : null,
                StartedOn = DateOnly.TryParse(Get("startedOn"), out var so) ? so : null,
                FinishedOn = DateOnly.TryParse(Get("finishedOn"), out var fo) ? fo : null,
                Notes = string.IsNullOrWhiteSpace(Get("notes")) ? null : Get("notes"),
            };
            if (Guid.TryParse(Get("id"), out var id)) book.Id = id;
            books.Add(book);
        }
        return Merge(library, books, null);
    }

    /// <summary>Minimal RFC 4180 reader: quoted fields, escaped quotes, CRLF.</summary>
    internal static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n') { row.Add(field.ToString()); field.Clear(); rows.Add(row); row = new List<string>(); }
            else if (c != '\r') field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }
}
