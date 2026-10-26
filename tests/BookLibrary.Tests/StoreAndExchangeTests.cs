using BookLibrary;
using Xunit;

namespace BookLibrary.Tests;

public class StoreAndExchangeTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"booklibrary-test-{Guid.NewGuid()}.json");

    [Fact]
    public void Store_RoundTripsBooksAndGoal()
    {
        var path = TempPath();
        try
        {
            var library = new Library();
            var book = library.Add(new Book { Title = "Dune", Author = "Frank Herbert", TotalPages = 688, Genre = "Sci-Fi" });
            var store = new JsonBookStore(path);
            store.Save(library, new ReadingGoal(2026, 24));

            var loaded = new JsonBookStore(path).Load();
            Assert.Single(loaded.Books);
            Assert.Equal(book.Id, loaded.Books[0].Id);
            Assert.Equal("Sci-Fi", loaded.Books[0].Genre);
            Assert.Equal(new ReadingGoal(2026, 24), loaded.Goal);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Store_QuarantinesCorruptFileInsteadOfCrashing()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{ this is not json");
            var store = new JsonBookStore(path);
            var data = store.Load();
            Assert.True(store.LastLoadWasCorrupt);
            Assert.Empty(data.Books);
            Assert.False(File.Exists(path));
            Assert.Single(Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + ".corrupt-*"));
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + ".corrupt-*")) File.Delete(f);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Exchange_JsonRoundTripSkipsDuplicates()
    {
        var library = new Library();
        library.Add(new Book { Title = "Dune", Author = "Frank Herbert", TotalPages = 688 });
        var json = DataExchange.ToJson(library, null);

        var other = new Library();
        var first = DataExchange.ImportJson(other, json);
        var second = DataExchange.ImportJson(other, json);
        Assert.Equal((1, 0), (first.Added, first.Skipped));
        Assert.Equal((0, 1), (second.Added, second.Skipped));
    }

    [Fact]
    public void Exchange_CsvRoundTripHandlesCommasInTitles()
    {
        var library = new Library();
        library.Add(new Book { Title = "Hello, World", Author = "Someone", TotalPages = 100, Tags = new List<string> { "a", "b" } });
        var csv = DataExchange.ToCsv(library);

        var other = new Library();
        var result = DataExchange.ImportCsv(other, csv);
        Assert.Equal(1, result.Added);
        Assert.Equal("Hello, World", other.Books[0].Title);
        Assert.Equal(new List<string> { "a", "b" }, other.Books[0].Tags);
    }

    [Fact]
    public void Exchange_CsvParserHandlesQuotedFieldsAndEscapedQuotes()
    {
        var rows = DataExchange.ParseCsv("a,b\r\n\"x, y\",\"say \"\"hi\"\"\"\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal("x, y", rows[1][0]);
        Assert.Equal("say \"hi\"", rows[1][1]);
    }
}
