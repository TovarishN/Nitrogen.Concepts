using Xunit;

namespace NitrogenCatalog.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Nitrogen_runtime_is_referenced() => Assert.NotNull(typeof(Nitrogen.Language));
}
