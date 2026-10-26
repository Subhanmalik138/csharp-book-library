namespace BookLibrary;

/// <summary>
/// One book in the library. All invariants live here so the CLI, the
/// store, and the tests can never construct an impossible book:
/// a rating outside 1–5, a current page past the end, or a finished
/// book with no finish date are all rejected, not silently fixed.
/// </summary>
public sealed class Book
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string? Isbn { get; set; }
    public int? Year { get; set; }
    public string? Genre { get; set; }
    public List<string> Tags { get; set; } = new();
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; }
    public ReadingStatus Status { get; set; } = ReadingStatus.WantToRead;
    public int? Rating { get; set; }
    public DateOnly? StartedOn { get; set; }
    public DateOnly? FinishedOn { get; set; }
    public string? Notes { get; set; }
    public DateTime AddedOn { get; set; } = DateTime.UtcNow;

    /// <summary>0–100, rounded. Books without a page count report 0.</summary>
    public int ProgressPercent =>
        TotalPages <= 0 ? 0 : Math.Min(100, (int)Math.Round(CurrentPage * 100.0 / TotalPages));

    public bool IsFinished => Status == ReadingStatus.Finished;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Title))
            throw new ArgumentException("Title is required.");
        if (string.IsNullOrWhiteSpace(Author))
            throw new ArgumentException("Author is required.");
        if (TotalPages < 0)
            throw new ArgumentException("Total pages cannot be negative.");
        if (CurrentPage < 0 || CurrentPage > TotalPages)
            throw new ArgumentException("Current page must be between 0 and total pages.");
        if (Rating is < 1 or > 5)
            throw new ArgumentException("Rating must be between 1 and 5.");
        if (Year is < 0 or > DateTime.UtcNow.Year + 1)
            throw new ArgumentException("Year is out of range.");
        if (Status == ReadingStatus.Finished && FinishedOn is null)
            throw new ArgumentException("A finished book needs a finish date.");
        if (FinishedOn is not null && StartedOn is not null && FinishedOn < StartedOn)
            throw new ArgumentException("Finish date cannot be before the start date.");
        if (Isbn is not null && Isbn.Replace("-", "").Replace(" ", "").Length is not (10 or 13))
            throw new ArgumentException("ISBN must be 10 or 13 digits (dashes allowed).");
    }

    /// <summary>
    /// Move the bookmark. Reaching the last page finishes the book;
    /// anything less keeps it in progress and stamps the start date
    /// on first movement.
    /// </summary>
    public void SetProgress(int page, DateOnly today)
    {
        if (page < 0 || page > TotalPages)
            throw new ArgumentException("Page must be between 0 and total pages.");
        CurrentPage = page;
        if (TotalPages > 0 && page >= TotalPages)
        {
            Status = ReadingStatus.Finished;
            FinishedOn ??= today;
        }
        else if (Status == ReadingStatus.WantToRead)
        {
            Status = ReadingStatus.Reading;
            StartedOn ??= today;
        }
    }

    public void Finish(DateOnly day)
    {
        if (TotalPages > 0) CurrentPage = TotalPages;
        Status = ReadingStatus.Finished;
        FinishedOn = day;
        StartedOn ??= day;
    }
}
