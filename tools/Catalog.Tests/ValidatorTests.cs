using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class ValidatorTests
{
    static (int Exit, string Output) Run(string root, params string[] args)
    {
        var output = new StringWriter();
        int exit = CatalogCli.Run(["--root", root, .. args], output);
        return (exit, output.ToString().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Written_index_validates()
    {
        using var temp = new TempCatalog();
        Assert.Equal(0, Run(temp.Root, "index", "--write").Exit);
        Assert.Equal((0, "Catalog valid\n"), Run(temp.Root, "validate"));
    }

    [Fact]
    public void Missing_index_fails_validation()
    {
        using var temp = new TempCatalog();
        var (exit, output) = Run(temp.Root, "validate");
        Assert.Equal(1, exit);
        Assert.Contains("CA0104", output);
    }

    [Fact]
    public void Index_is_not_checked_while_records_have_errors()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/duplicate.ncat", Samples.Concept());
        var (exit, output) = Run(temp.Root, "validate");
        Assert.Equal(1, exit);
        Assert.Contains("NB0003", output);
        Assert.DoesNotContain("CA0104", output);
    }

    [Fact]
    public void Index_is_not_written_while_records_have_errors()
    {
        using var temp = new TempCatalog();
        temp.Write("concepts/duplicate.ncat", Samples.Concept());
        Assert.Equal(1, Run(temp.Root, "index", "--write").Exit);
        Assert.False(File.Exists(Path.Combine(temp.Root, "index.json")));
    }

    [Fact]
    public void Base_comparison_runs()
    {
        using var old = new TempCatalog();
        old.Write("concepts/single-flight.ncat", Samples.Concept("observation"));
        using var temp = new TempCatalog();
        temp.Write("concepts/single-flight.ncat", Samples.Concept("established"));
        var (exit, output) = Run(temp.Root, "validate", "--base", old.Root);
        Assert.Equal(1, exit);
        Assert.Contains("CA0200", output);
    }

    [Fact]
    public void Unknown_command_prints_usage()
    {
        using var temp = new TempCatalog();
        var (exit, output) = Run(temp.Root, "frobnicate");
        Assert.Equal(2, exit);
        Assert.StartsWith("usage:", output);
    }

    [Fact]
    public void Repository_catalog_is_valid_and_seed_is_discoverable()
    {
        string root = RepositoryRoot();
        Assert.Equal((0, "Catalog valid\n"), Run(root, "validate"));
        using var catalog = CatalogLoader.Load(root);
        var entry = IndexBuilder.Build(CatalogModel.Read(catalog))["capabilities"]!["Concurrency.CoalesceInFlight"]!;
        Assert.Equal("Concurrency.SingleFlight", entry["concepts"]![0]!.GetValue<string>());
        Assert.Empty(entry["realizations"]!.AsArray());
    }

    static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Catalog.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Catalog.slnx not found");
    }
}
