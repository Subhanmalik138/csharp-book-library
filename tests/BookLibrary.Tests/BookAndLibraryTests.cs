using BookLibrary;
using Xunit;

namespace BookLibrary.Tests;

public class BookAndLibraryTests
{
    private static Book Sample(string title = "Dune", string author = "Frank Herbert", int pages = 688) =>
        new() { Title = title, Author = author, TotalPages = pages };

    [Fact]
    public void ProgressPercent_RoundsAndCaps()
    {
        var book = Sample();
        book.CurrentPage = 344;
        Assert.Equal(50, book.ProgressPercent);
        book.CurrentPage = 688;
        Assert.Equal(100, book.ProgressPercent);
    }

    [Fact]
    public void SetProgress_ToLastPage_FinishesBook()
    {
        var book = Sample();
        var today = new DateOnly(2026, 10, 26);
        book.SetProgress(688, today);
        Assert.Equal(ReadingStatus.Finished, book.Status);
        Assert.Equal(today, book.FinishedOn);
        Assert.Equal(today, book.StartedOn);
    }

    [Fact]
    public void SetProgress_Partial_MarksReadingAndStampsStart()
    {
        var book = Sample();
        book.SetProgress(10, new DateOnly(2026, 10, 26));
        Assert.Equal(ReadingStatus.Reading, book.Status);
        Assert.NotNull(book.StartedOn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Validate_RejectsRatingOutside1To5(int rating)
    {
        var book = Sample();
        book.Rating = rating;
        Assert.Throws<ArgumentException>(() => book.Validate());
    }

    [Fact]
    public void Validate_RejectsPageBeyondTotal()
    {
        var book = Sample();
        book.CurrentPage = 689;
        Assert.Throws<ArgumentException>(() => book.Validate());
    }

    [Fact]
    public void Validate_RejectsFinishedBookWithoutFinishDate()
    {
        var book = Sample();
        book.Status = ReadingStatus.Finished;
        Assert.Throws<ArgumentException>(() => book.Validate());
    }

    [Fact]
    public void Add_RejectsDuplicateByIsbnEvenWithDifferentTitle()
    {
        var library = new Library();
        library.Add(new Book { Title = "Dune", Author = "Frank Herbert", Isbn = "978-0441172719", TotalPages = 688 });
        Assert.Throws<InvalidOperationException>(() =>
            library.Add(new Book { Title = "Dune (other edition)", Author = "Someone Else", Isbn = "9780441172719", TotalPages = 100 }));
    }

    [Fact]
    public void Add_RejectsDuplicateByTitleAndAuthorCaseInsensitive()
    {
        var library = new Library();
        library.Add(Sample());
        Assert.Throws<InvalidOperationException>(() => library.Add(Sample("dune", "frank herbert")));
    }

    [Fact]
    public void Search_FiltersByQueryStatusAndRating()
    {
        var library = new Library();
        library.Add(Sample());
        var hobbit = Sample("The Hobbit", "J.R.R. Tolkien", 310);
        hobbit.Status = ReadingStatus.Finished;
        hobbit.FinishedOn = new DateOnly(2026, 1, 5);
        hobbit.Rating = 5;
        library.Add(hobbit);

        Assert.Single(library.Search("hobbit"));
        Assert.Single(library.Search(status: ReadingStatus.Finished));
        Assert.Single(library.Search(minRating: 5));
        Assert.Empty(library.Search("nonexistent"));
    }

    [Fact]
    public void Resolve_FindsBookByIdPrefix()
    {
        var library = new Library();
        var book = library.Add(Sample());
        Assert.Same(book, library.Resolve(book.Id.ToString()[..8]));
        Assert.Null(library.Resolve("deadbeef"));
    }

    [Fact]
    public void Stats_CountsStatusesPagesAndAverageRating()
    {
        var library = new Library();
        var a = Sample();
        a.Status = ReadingStatus.Reading;
        a.CurrentPage = 100;
        a.Rating = 4;
        library.Add(a);
        var b = Sample("The Hobbit", "J.R.R. Tolkien", 310);
        b.Status = ReadingStatus.Finished;
        b.FinishedOn = new DateOnly(2026, 1, 5);
        b.CurrentPage = 310;
        b.Rating = 5;
        library.Add(b);

        var stats = library.Stats();
        Assert.Equal(2, stats.Total);
        Assert.Equal(1, stats.Reading);
        Assert.Equal(1, stats.Finished);
        Assert.Equal(410, stats.PagesRead);
        Assert.Equal(4.5, stats.AverageRating);
        Assert.Equal("The Hobbit", stats.TopRated!.Title);
    }

    [Fact]
    public void ReadingGoal_CountsOnlyBooksFinishedInItsYear()
    {
        var goal = new ReadingGoal(2026, 10);
        var books = new List<Book>
        {
            new() { Title = "A", Author = "A", Status = ReadingStatus.Finished, FinishedOn = new DateOnly(2026, 3, 1) },
            new() { Title = "B", Author = "B", Status = ReadingStatus.Finished, FinishedOn = new DateOnly(2025, 12, 31) },
            new() { Title = "C", Author = "C", Status = ReadingStatus.Reading },
        };
        Assert.Equal(1, goal.FinishedCount(books));
        Assert.Equal(10, goal.PercentComplete(books));
    }
}
