namespace BookLibrary;

/// <summary>
/// The in-memory library: the single place books are added, removed,
/// searched, and summarised. Persistence and the CLI both go through
/// here, so the rules are enforced exactly once.
/// </summary>
public sealed class Library
{
    private readonly List<Book> _books = new();

    public IReadOnlyList<Book> Books => _books.AsReadOnly();

    public Book Add(Book book)
    {
        book.Validate();
        var duplicate = FindDuplicate(book);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Already in the library: \"{duplicate.Title}\" by {duplicate.Author} (id {duplicate.Id}).");
        _books.Add(book);
        return book;
    }

    /// <summary>
    /// ISBN wins when both books carry one; otherwise title + author
    /// (case-insensitive) is the identity. Import uses the same rule,
    /// so re-importing a library never doubles it.
    /// </summary>
    public Book? FindDuplicate(Book book)
    {
        if (!string.IsNullOrWhiteSpace(book.Isbn))
        {
            var byIsbn = _books.FirstOrDefault(b =>
                string.Equals(NormalizeIsbn(b.Isbn), NormalizeIsbn(book.Isbn), StringComparison.Ordinal));
            if (byIsbn is not null) return byIsbn;
        }
        return _books.FirstOrDefault(b =>
            string.Equals(b.Title.Trim(), book.Title.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(b.Author.Trim(), book.Author.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static string? NormalizeIsbn(string? isbn) =>
        isbn is null ? null : isbn.Replace("-", "").Replace(" ", "");

    public bool Remove(Guid id) => _books.RemoveAll(b => b.Id == id) > 0;

    public Book? Get(Guid id) => _books.FirstOrDefault(b => b.Id == id);

    /// <summary>Resolve a full id or an unambiguous id prefix.</summary>
    public Book? Resolve(string idOrPrefix)
    {
        if (Guid.TryParse(idOrPrefix, out var full)) return Get(full);
        var matches = _books.Where(b => b.Id.ToString().StartsWith(idOrPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// Substring search across title, author, genre, and tags, with
    /// optional status / genre / author / minimum-rating filters.
    /// An empty query matches everything, so `list` is just search
    /// with no arguments.
    /// </summary>
    public IEnumerable<Book> Search(
        string? query = null,
        ReadingStatus? status = null,
        string? genre = null,
        string? author = null,
        int? minRating = null)
    {
        IEnumerable<Book> result = _books;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            result = result.Where(b =>
                b.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.Author.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (b.Genre?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                b.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }
        if (status is not null) result = result.Where(b => b.Status == status);
        if (!string.IsNullOrWhiteSpace(genre))
            result = result.Where(b => string.Equals(b.Genre, genre.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(author))
            result = result.Where(b => b.Author.Contains(author.Trim(), StringComparison.OrdinalIgnoreCase));
        if (minRating is not null) result = result.Where(b => (b.Rating ?? 0) >= minRating);
        return result;
    }

    public IEnumerable<Book> Sorted(IEnumerable<Book> books, string sort) => sort.ToLowerInvariant() switch
    {
        "author" => books.OrderBy(b => b.Author).ThenBy(b => b.Title),
        "rating" => books.OrderByDescending(b => b.Rating ?? 0).ThenBy(b => b.Title),
        "progress" => books.OrderByDescending(b => b.ProgressPercent).ThenBy(b => b.Title),
        "recent" => books.OrderByDescending(b => b.AddedOn),
        _ => books.OrderBy(b => b.Title),
    };

    public LibraryStats Stats() => LibraryStats.Compute(_books);
}

/// <summary>Aggregate numbers for `stats` and the reading goal.</summary>
public sealed record LibraryStats(
    int Total,
    int WantToRead,
    int Reading,
    int Finished,
    int DidNotFinish,
    int PagesRead,
    int TotalPages,
    double? AverageRating,
    IReadOnlyDictionary<string, int> ByGenre,
    Book? TopRated)
{
    public static LibraryStats Compute(IReadOnlyList<Book> books)
    {
        var ratings = books.Where(b => b.Rating is not null).Select(b => b.Rating!.Value).ToList();
        var byGenre = books
            .Where(b => !string.IsNullOrWhiteSpace(b.Genre))
            .GroupBy(b => b.Genre!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        return new LibraryStats(
            books.Count,
            books.Count(b => b.Status == ReadingStatus.WantToRead),
            books.Count(b => b.Status == ReadingStatus.Reading),
            books.Count(b => b.Status == ReadingStatus.Finished),
            books.Count(b => b.Status == ReadingStatus.DidNotFinish),
            books.Sum(b => b.CurrentPage),
            books.Sum(b => b.TotalPages),
            ratings.Count > 0 ? ratings.Average() : null,
            byGenre,
            books.Where(b => b.Rating is not null).OrderByDescending(b => b.Rating).ThenBy(b => b.Title).FirstOrDefault());
    }
}
