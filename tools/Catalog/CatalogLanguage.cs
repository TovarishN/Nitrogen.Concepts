using Nitrogen;
using NitrogenCatalog.Syntax;

namespace NitrogenCatalog;

/// <summary>The catalog language built from Catalog.ngr, and where its records live.</summary>
public static class CatalogLanguage
{
    public const string Extension = ".ncat";

    public static readonly IReadOnlyDictionary<string, string> KindOfDirectory = new Dictionary<string, string>
    {
        ["concepts"] = "concept",
        ["capabilities"] = "capability",
        ["realizations"] = "realization",
        ["evidence"] = "evidence",
    };

    public static Language Instance { get; } = new LanguageBuilder().Add(CatalogModule.Instance).Build();

    public static Rule Start => CatalogModule.Record;
}
