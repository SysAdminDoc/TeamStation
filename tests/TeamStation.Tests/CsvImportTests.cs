using TeamStation.Core.Models;
using TeamStation.Core.Serialization;

namespace TeamStation.Tests;

public class CsvImportTests
{
    private static readonly IReadOnlyList<Folder> NoExistingFolders = new List<Folder>();

    [Fact]
    public void Parse_basic_csv_produces_entries_and_autocreates_folder()
    {
        var csv = """
                  Group,Alias,TeamViewer_ID,Password,Notes,Tags
                  Customer A,Reception PC,123456789,secret-1,Front desk,front-of-house
                  Lab,Test box,345678901,lab-pw,,"dev,test"
                  """;
        var result = CsvImport.Parse(csv, NoExistingFolders);

        Assert.Empty(result.Errors);
        Assert.Empty(result.Skipped);
        Assert.Equal(2, result.Folders.Count);
        Assert.Equal(2, result.Entries.Count);
        Assert.Contains(result.Folders, f => f.Name == "Customer A");
        Assert.Contains(result.Folders, f => f.Name == "Lab");

        var reception = result.Entries.First(e => e.Name == "Reception PC");
        Assert.Equal("123456789", reception.TeamViewerId);
        Assert.Equal("secret-1", reception.Password);
        Assert.Equal("Front desk", reception.Notes);
        Assert.Single(reception.Tags);
        Assert.Contains("front-of-house", reception.Tags);

        var testBox = result.Entries.First(e => e.Name == "Test box");
        Assert.Contains("dev", testBox.Tags);
        Assert.Contains("test", testBox.Tags);
    }

    // Covers the v0.1.1 fix: header "Friendly Name" must now match alias
    // "friendly_name" after underscore-stripping normalisation.
    [Theory]
    [InlineData("Friendly Name")]
    [InlineData("friendly_name")]
    [InlineData("FRIENDLYNAME")]
    [InlineData("Friendly-Name")]   // hyphen stripped
    public void Parse_matches_name_aliases_across_separator_styles(string header)
    {
        var csv = $"Id,{header}\n123456789,A\n";
        var result = CsvImport.Parse(csv, NoExistingFolders);
        Assert.Empty(result.Errors);
        Assert.Single(result.Entries);
        Assert.Equal("A", result.Entries[0].Name);
    }

    // Covers the v0.1.1 fix: ID column aliases now tolerate spaces.
    [Theory]
    [InlineData("TeamViewer ID")]
    [InlineData("TeamViewer_ID")]
    [InlineData("TEAMVIEWERID")]
    [InlineData("TV ID")]
    [InlineData("Remote Control ID")]
    [InlineData("Device ID")]
    public void Parse_matches_id_aliases_across_separator_styles(string header)
    {
        var csv = $"Name,{header}\nA,123456789\n";
        var result = CsvImport.Parse(csv, NoExistingFolders);
        Assert.Empty(result.Errors);
        Assert.Single(result.Entries);
        Assert.Equal("123456789", result.Entries[0].TeamViewerId);
    }

    [Fact]
    public void Parse_without_id_column_returns_error_and_no_entries()
    {
        var csv = "Alias,Password\nBob,secret\n";
        var result = CsvImport.Parse(csv, NoExistingFolders);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(result.Entries);
        Assert.Empty(result.Folders);
    }

    [Fact]
    public void Parse_skips_rows_with_non_numeric_ids_and_reports_them()
    {
        var csv = """
                  Id,Alias
                  123456789,Good
                  abc,Bad letters
                  ,Bad empty
                  123,Bad too short
                  """;
        var result = CsvImport.Parse(csv, NoExistingFolders);

        Assert.Empty(result.Errors);
        Assert.Single(result.Entries);
        Assert.Equal(3, result.Skipped.Count); // 3 skip reasons
    }

    [Fact]
    public void Parse_deduplicates_folders_within_and_against_existing()
    {
        var existing = new List<Folder> { new Folder { Name = "Customer A" } };
        var csv = """
                  Group,Id,Alias
                  Customer A,111222333,one
                  customer a,222333444,two-matches-existing-case-insensitively
                  Customer B,333444555,three
                  Customer B,444555666,four
                  """;
        var result = CsvImport.Parse(csv, existing);
        Assert.Empty(result.Errors);
        // Only "Customer B" is new; "Customer A" already exists.
        Assert.Single(result.Folders);
        Assert.Equal("Customer B", result.Folders[0].Name);
        // All four entries parsed.
        Assert.Equal(4, result.Entries.Count);
    }

    [Fact]
    public void Parse_handles_quoted_fields_and_escaped_quotes_and_embedded_commas()
    {
        // Raw-string literals can't contain "" on the content side because the
        // closing delimiter must sit alone on a line, so use an escaped string here.
        var csv = "Id,Notes,Tags\n123456789,\"She said, \"\"hi\"\"\",\"a,b,c\"\n";
        var result = CsvImport.Parse(csv, NoExistingFolders);
        Assert.Empty(result.Errors);
        Assert.Single(result.Entries);
        Assert.Equal("She said, \"hi\"", result.Entries[0].Notes);
        Assert.Equal(new[] { "a", "b", "c" }, result.Entries[0].Tags);
    }

    [Fact]
    public void Parse_deduplicates_tags_case_insensitively()
    {
        var csv = """
                  Id,Tags
                  123456789,"dev,DEV,prod,Dev"
                  """;
        var result = CsvImport.Parse(csv, NoExistingFolders);
        Assert.Empty(result.Errors);
        Assert.Single(result.Entries);
        Assert.Equal(2, result.Entries[0].Tags.Count);
    }

    [Fact]
    public void Parse_blank_rows_are_ignored()
    {
        var csv = "Id,Alias\n\n123456789,A\n\n,\n234567890,B\n";
        var result = CsvImport.Parse(csv, NoExistingFolders);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Skipped);
        Assert.Equal(2, result.Entries.Count);
    }
}
