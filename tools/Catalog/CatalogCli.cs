namespace NitrogenCatalog;

public static class CatalogCli
{
    const string Usage = "usage: nitrogen-catalog [--root PATH] (validate [--base PATH] | index --write)";

    public static int Run(string[] args, TextWriter output)
    {
        string root = Directory.GetCurrentDirectory();
        string? baseRoot = null;
        var words = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--root" && i + 1 < args.Length) root = args[++i];
            else if (args[i] == "--base" && i + 1 < args.Length) baseRoot = args[++i];
            else words.Add(args[i]);
        }

        IReadOnlyList<CatalogDiagnostic> diagnostics;
        if (words is ["validate"]) diagnostics = CatalogValidator.Validate(root, baseRoot);
        else if (words is ["index", "--write"] && baseRoot is null) diagnostics = CatalogValidator.WriteIndex(root);
        else
        {
            output.WriteLine(Usage);
            return 2;
        }

        foreach (var diagnostic in diagnostics) output.WriteLine(diagnostic);
        if (diagnostics.Count > 0) return 1;
        output.WriteLine(words[0] == "validate" ? "Catalog valid" : "Index written");
        return 0;
    }
}
