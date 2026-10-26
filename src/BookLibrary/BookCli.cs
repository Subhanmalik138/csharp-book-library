namespace BookLibrary;

/// <summary>
/// Argument parsing and command dispatch. The CLI is deliberately
/// thin: every rule lives in <see cref="Book"/> / <see cref="Library"/>,
/// so this file only translates argv into calls and prints results.
/// Writers and the clock are injected, which is what makes the CLI
/// testable without a subprocess.
/// </summary>
public sealed class BookCli
{
    private readonly TextWriter _out;
    private readonly TextReader _in;
    private readonly Func<DateOnly> _today;

    public BookCli(TextWriter output, TextReader input, Func<DateOnly>? today = null)
    {
        _out = output;
        _in = input;
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
    }

    public int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            var store = new JsonBookStore(DataPaths.Default());
            var data = store.Load();
            if (store.LastLoadWasCorrupt)
                _out.WriteLine("Warning: data file was corrupt and was quarantined; starting from an empty library.");
            var library = new Library();
            foreach (var book in data.Books) library.Add(book);
            var goal = data.Goal;
            var mutated = Dispatch(args[0], args.Skip(1).ToArray(), library, ref goal);
            if (mutated) store.Save(library, goal);
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            _out.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private bool Dispatch(string command, string[] args, Library library, ref ReadingGoal? goal)
    {
        var (pos, opts, flags) = Parse(args);
        switch (command)
        {
            case "add":
            {
                if (pos.Count == 0) throw new ArgumentException("Usage: add <title> --author <name>");
                var book = new Book
                {
                    Title = pos[0],
                    Author = Required(opts, "author"),
                    Isbn = Opt(opts, "isbn"),
                    Year = IntOpt(opts, "year"),
                    Genre = Opt(opts, "genre"),
                    Tags = Opt(opts, "tags")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>(),
                    TotalPages = IntOpt(opts, "pages") ?? 0,
                    Status = Opt(opts, "status") is string s ? ParseStatus(s) : ReadingStatus.WantToRead,
                    Rating = IntOpt(opts, "rating"),
                    Notes = Opt(opts, "notes"),
                };
                library.Add(book);
                _out.WriteLine($"Added: {book.Id} \"{book.Title}\" by {book.Author}");
                return true;
            }
            case "list":
            {
                var books = library.Sorted(library.Search(
                    Opt(opts, "query"),
                    Opt(opts, "status") is string st ? ParseStatus(st) : null,
                    Opt(opts, "genre"), Opt(opts, "author"), IntOpt(opts, "min-rating")),
                    Opt(opts, "sort") ?? "title");
                PrintBooks(books);
                return false;
            }
            case "search":
            {
                if (pos.Count == 0) throw new ArgumentException("Usage: search <query>");
                PrintBooks(library.Search(pos[0]));
                return false;
            }
            case "show":
            {
                var book = Resolve(library, pos);
                PrintBook(book);
                return false;
            }
            case "remove":
            {
                var book = Resolve(library, pos);
                if (!flags.Contains("force"))
                {
                    _out.Write($"Remove \"{book.Title}\" by {book.Author}? [y/N] ");
                    if ((_in.ReadLine() ?? "").Trim().ToLowerInvariant() != "y")
                    {
                        _out.WriteLine("Aborted.");
                        return false;
                    }
                }
                library.Remove(book.Id);
                _out.WriteLine($"Removed: \"{book.Title}\"");
                return true;
            }
            case "progress":
            {
                var book = Resolve(library, pos);
                if (pos.Count < 2) throw new ArgumentException("Usage: progress <id> <page>");
                book.SetProgress(int.Parse(pos[1]), _today());
                book.Validate();
                _out.WriteLine($"\"{book.Title}\": page {book.CurrentPage}/{book.TotalPages} ({book.ProgressPercent}%) [{book.Status}]");
                return true;
            }
            case "finish":
            {
                var book = Resolve(library, pos);
                book.Finish(Opt(opts, "date") is string d ? DateOnly.Parse(d) : _today());
                _out.WriteLine($"Finished: \"{book.Title}\"");
                return true;
            }
            case "update":
            {
                var book = Resolve(library, pos);
                if (Opt(opts, "title") is string t) book.Title = t;
                if (Opt(opts, "author") is string a) book.Author = a;
                if (Opt(opts, "genre") is string g) book.Genre = g;
                if (Opt(opts, "isbn") is string i) book.Isbn = i;
                if (IntOpt(opts, "year") is int y) book.Year = y;
                if (IntOpt(opts, "pages") is int p) book.TotalPages = p;
                if (IntOpt(opts, "rating") is int r) book.Rating = r;
                if (Opt(opts, "tags") is string tg) book.Tags = tg.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
                if (Opt(opts, "notes") is string n) book.Notes = n;
                if (Opt(opts, "status") is string s)
                {
                    book.Status = ParseStatus(s);
                    if (book.Status == ReadingStatus.Finished && book.FinishedOn is null)
                        book.FinishedOn = _today();
                }
                book.Validate();
                _out.WriteLine($"Updated: \"{book.Title}\"");
                return true;
            }
            case "export":
            {
                if (pos.Count == 0) throw new ArgumentException("Usage: export <path> [--format json|csv]");
                var format = (Opt(opts, "format") ?? (pos[0].EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? "csv" : "json")).ToLowerInvariant();
                var text = format == "csv" ? DataExchange.ToCsv(library) : DataExchange.ToJson(library, goal);
                File.WriteAllText(pos[0], text);
                _out.WriteLine($"Exported {library.Books.Count} books to {pos[0]} ({format})");
                return false;
            }
            case "import":
            {
                if (pos.Count == 0) throw new ArgumentException("Usage: import <path> [--format json|csv]");
                var format = (Opt(opts, "format") ?? (pos[0].EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? "csv" : "json")).ToLowerInvariant();
                var text = File.ReadAllText(pos[0]);
                var result = format == "csv" ? DataExchange.ImportCsv(library, text) : DataExchange.ImportJson(library, text);
                if (result.Goal is not null && goal is null) goal = result.Goal;
                _out.WriteLine($"Imported {result.Added} books, skipped {result.Skipped} duplicates.");
                return result.Added > 0 || (result.Goal is not null && goal == result.Goal);
            }
            case "stats":
            {
                PrintStats(library);
                return false;
            }
            case "goal":
            {
                if (pos.Count >= 1 && pos[0] == "set")
                {
                    if (pos.Count < 2) throw new ArgumentException("Usage: goal set <books> [--year <year>]");
                    goal = new ReadingGoal(IntOpt(opts, "year") ?? _today().Year, int.Parse(pos[1]));
                    _out.WriteLine($"Goal set: {goal.Target} books in {goal.Year}");
                    return true;
                }
                if (pos.Count >= 1 && pos[0] == "clear")
                {
                    goal = null;
                    _out.WriteLine("Goal cleared.");
                    return true;
                }
                if (goal is null) { _out.WriteLine("No reading goal set. Use: goal set <books>"); return false; }
                _out.WriteLine($"Goal {goal.Year}: {goal.FinishedCount(library.Books)} / {goal.Target} books ({goal.PercentComplete(library.Books)}%)");
                return false;
            }
            default:
                throw new ArgumentException($"Unknown command: {command}. Run with --help.");
        }
    }

    private static Book Resolve(Library library, List<string> pos)
    {
        if (pos.Count == 0) throw new ArgumentException("A book id is required.");
        return library.Resolve(pos[0])
            ?? throw new ArgumentException($"No book matches id '{pos[0]}'.");
    }

    private void PrintBooks(IEnumerable<Book> books)
    {
        var list = books.ToList();
        if (list.Count == 0) { _out.WriteLine("No books match."); return; }
        foreach (var b in list)
            _out.WriteLine($"{b.Id.ToString()[..8]}  {b.Title} — {b.Author}  [{b.Status}]  {b.ProgressPercent}%{(b.Rating is not null ? $"  ★{b.Rating}" : "")}");
    }

    private void PrintBook(Book b)
    {
        _out.WriteLine($"{b.Title} — {b.Author}");
        _out.WriteLine($"  id:     {b.Id}");
        _out.WriteLine($"  status: {b.Status}  progress: {b.CurrentPage}/{b.TotalPages} ({b.ProgressPercent}%)");
        if (b.Isbn is not null) _out.WriteLine($"  isbn:   {b.Isbn}");
        if (b.Year is not null) _out.WriteLine($"  year:   {b.Year}");
        if (b.Genre is not null) _out.WriteLine($"  genre:  {b.Genre}");
        if (b.Tags.Count > 0) _out.WriteLine($"  tags:   {string.Join(", ", b.Tags)}");
        if (b.Rating is not null) _out.WriteLine($"  rating: ★{b.Rating}");
        if (b.StartedOn is not null) _out.WriteLine($"  started: {b.StartedOn:yyyy-MM-dd}");
        if (b.FinishedOn is not null) _out.WriteLine($"  finished: {b.FinishedOn:yyyy-MM-dd}");
        if (b.Notes is not null) _out.WriteLine($"  notes:  {b.Notes}");
    }

    private void PrintStats(Library library)
    {
        var s = library.Stats();
        _out.WriteLine($"Books: {s.Total}  (want: {s.WantToRead}, reading: {s.Reading}, finished: {s.Finished}, DNF: {s.DidNotFinish})");
        _out.WriteLine($"Pages: {s.PagesRead} read of {s.TotalPages} total");
        if (s.AverageRating is not null) _out.WriteLine($"Average rating: {s.AverageRating:F2} ★");
        if (s.TopRated is not null) _out.WriteLine($"Top rated: \"{s.TopRated.Title}\" by {s.TopRated.Author} (★{s.TopRated.Rating})");
        foreach (var (genre, count) in s.ByGenre.OrderByDescending(g => g.Value))
            _out.WriteLine($"  {genre}: {count}");
    }

    private static ReadingStatus ParseStatus(string s) => s.ToLowerInvariant() switch
    {
        "want" or "wanttoread" or "to-read" => ReadingStatus.WantToRead,
        "reading" or "in-progress" => ReadingStatus.Reading,
        "finished" or "done" => ReadingStatus.Finished,
        "dnf" or "didnotfinish" or "abandoned" => ReadingStatus.DidNotFinish,
        _ => throw new ArgumentException($"Unknown status: {s} (want, reading, finished, dnf)"),
    };

    private static (List<string>, Dictionary<string, string>, HashSet<string>) Parse(string[] args)
    {
        var pos = new List<string>();
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                var key = args[i][2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    opts[key] = args[++i];
                else
                    flags.Add(key);
            }
            else pos.Add(args[i]);
        }
        return (pos, opts, flags);
    }

    private static string? Opt(Dictionary<string, string> opts, string key) =>
        opts.TryGetValue(key, out var v) ? v : null;

    private static string Required(Dictionary<string, string> opts, string key) =>
        Opt(opts, key) ?? throw new ArgumentException($"--{key} is required.");

    private static int? IntOpt(Dictionary<string, string> opts, string key) =>
        Opt(opts, key) is string v ? int.Parse(v) : null;

    private void PrintHelp()
    {
        _out.WriteLine("""
            BookLibrary — personal book library manager

            Usage: booklibrary <command> [options]

              add <title> --author <name> [--isbn <isbn>] [--year <n>] [--genre <g>]
                  [--pages <n>] [--tags <a,b>] [--status <want|reading|finished|dnf>]
                  [--rating <1-5>] [--notes <text>]
              list [--query <q>] [--status <s>] [--genre <g>] [--author <a>]
                  [--min-rating <n>] [--sort <title|author|rating|progress|recent>]
              search <query>
              show <id>
              update <id> [--title ...] [--author ...] [--genre ...] [--isbn ...]
                  [--year ...] [--pages ...] [--rating ...] [--tags ...] [--notes ...]
                  [--status ...]
              progress <id> <page>
              finish <id> [--date <yyyy-MM-dd>]
              remove <id> [--force]
              stats
              goal [set <books> [--year <year>] | clear]
              export <path> [--format json|csv]
              import <path> [--format json|csv]

            Data lives in ~/.booklibrary/books.json;
            override the full path with BOOKLIBRARY_DATA.
            """);
    }
}
