using BookLibrary;
using Xunit;

namespace BookLibrary.Tests;

/// <summary>
/// CLI tests run through BookCli with a temp data file (via the
/// BOOKLIBRARY_DATA env var) — no subprocess, no real home directory.
/// Tests in one class run sequentially, so the env var is safe here.
/// </summary>
public class CliTests
{
    private static (BookCli Cli, StringWriter Out) Setup(string path)
    {
        Environment.SetEnvironmentVariable("BOOKLIBRARY_DATA", path);
        var output = new StringWriter();
        var cli = new BookCli(output, new StringReader(""), () => new DateOnly(2026, 10, 26));
        return (cli, output);
    }

    [Fact]
    public void Cli_AddListAndProgress_WorkEndToEnd()
    {
        var path = Path.Combine(Path.GetTempPath(), $"booklibrary-cli-{Guid.NewGuid()}.json");
        try
        {
            var (cli, output) = Setup(path);
            Assert.Equal(0, cli.Run(new[] { "add", "Dune", "--author", "Frank Herbert", "--pages", "688" }));
            var listOut = new StringWriter();
            var listCli = new BookCli(listOut, new StringReader(""), () => new DateOnly(2026, 10, 26));
            Assert.Equal(0, listCli.Run(new[] { "list" }));
            Assert.Contains("Dune", listOut.ToString());

            // List lines look like "<8-char id>  Title — Author ...".
            var id = listOut.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Split(' ')[0];
            Assert.Equal(0, cli.Run(new[] { "progress", id, "344" }));
            Assert.Contains("50%", output.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("BOOKLIBRARY_DATA", null);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Cli_DuplicateAdd_ReturnsErrorCode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"booklibrary-cli-{Guid.NewGuid()}.json");
        try
        {
            var (cli, output) = Setup(path);
            Assert.Equal(0, cli.Run(new[] { "add", "Dune", "--author", "Frank Herbert" }));
            Assert.Equal(1, cli.Run(new[] { "add", "Dune", "--author", "Frank Herbert" }));
            Assert.Contains("Already in the library", output.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("BOOKLIBRARY_DATA", null);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
