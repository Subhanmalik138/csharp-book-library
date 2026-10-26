# BookLibrary — personal book library manager CLI

A dependency-free .NET 8 CLI for tracking the books you own, read, and
want to read — with reading progress, ratings, a yearly reading goal,
stats, and JSON/CSV import and export. No database, no account: your
whole library is one JSON file you can back up, move, and share.

## Why this exists

Most reading trackers are either a spreadsheet you outgrow (no progress
math, no deduplication, no goal tracking) or a web service that owns
your data. BookLibrary is the middle ground: a flat file with real
rules — a book can't be rated 6 out of 5, a bookmark can't sit past
the last page, and importing the same library twice never doubles it.

## Design decisions

1. **Identity is ISBN first, title + author second.** Two editions of
   the same book share an ISBN; two books that happen to share a title
   don't share an author. Import and `add` use the same rule, so
   re-importing a library is a no-op instead of a mess.
2. **All invariants live in the domain, not the CLI.** `Book.Validate`
   and `Library.Add` reject impossible state; the CLI only translates
   argv into calls. That keeps the rules testable without a subprocess
   and makes a bad command fail loudly instead of writing bad data.
3. **Atomic JSON persistence.** Saves go to a temp file in the same
   directory and are renamed over the original, so a crash mid-save
   can't corrupt the library. A corrupt data file is quarantined to
   `books.json.corrupt-<utc>` and reported, never silently dropped.
4. **The reading goal is derived, never stored as progress.** The goal
   stores only a year and a target; progress counts books finished in
   that year. There is no counter to drift out of sync with the books.
5. **Progress drives status.** Moving a bookmark past page 0 starts
   the book; reaching the last page finishes it and stamps both dates.
   Status is a consequence of what you actually read, not a field you
   have to remember to update twice.

## Requirements

- .NET 8 SDK

## How to run

```bash
dotnet build BookLibrary.sln -c Release
dotnet src/BookLibrary/bin/Release/net8.0/BookLibrary.dll --help
```

Data lives in `~/.booklibrary/books.json`; override the full file path
with the `BOOKLIBRARY_DATA` environment variable.

## Usage

```bash
# add books
booklibrary add "Dune" --author "Frank Herbert" --year 1965 \
    --genre "Sci-Fi" --pages 688 --tags classic,desert
booklibrary add "The Hobbit" --author "J.R.R. Tolkien" --pages 310

# browse and search
booklibrary list --sort rating
booklibrary list --status reading --genre "Sci-Fi"
booklibrary search dune

# track reading
booklibrary progress <id> 344          # id prefixes work, if unambiguous
booklibrary finish <id> --date 2026-10-26
booklibrary update <id> --rating 5 --notes "Reread every year."

# stats and goals
booklibrary stats
booklibrary goal set 24 --year 2026
booklibrary goal

# take your data with you (re-importing skips duplicates)
booklibrary export library.json
booklibrary export library.csv
booklibrary import library.json
booklibrary remove <id> --force
```

## How to run tests

```bash
dotnet test BookLibrary.sln -c Release
```

20 xUnit tests cover the domain rules, duplicate detection, search and
stats, atomic store round-trips and corrupt-file quarantine, JSON/CSV
import and export, and the CLI end to end.

## Tech stack

- C# 12 / .NET 8, no runtime dependencies
- System.Text.Json for the data file and JSON exchange
- xUnit for tests
