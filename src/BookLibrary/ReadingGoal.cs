namespace BookLibrary;

/// <summary>A yearly "read N books" challenge. Progress is derived, never
/// stored: it counts books finished in the goal year.</summary>
public sealed record ReadingGoal(int Year, int Target)
{
    public int FinishedCount(IEnumerable<Book> books) =>
        books.Count(b => b.Status == ReadingStatus.Finished &&
                         b.FinishedOn is not null && b.FinishedOn.Value.Year == Year);

    public int Remaining(IEnumerable<Book> books) => Math.Max(0, Target - FinishedCount(books));

    public int PercentComplete(IEnumerable<Book> books) =>
        Target <= 0 ? 0 : Math.Min(100, FinishedCount(books) * 100 / Target);
}
