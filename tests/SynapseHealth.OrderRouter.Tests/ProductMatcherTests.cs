using SynapseHealth.OrderRouter.Models;
using SynapseHealth.OrderRouter.Services;
using FluentAssertions;

namespace SynapseHealth.OrderRouter.Tests;

public class ProductMatcherTests
{
    private static ProductMatcher CreateMatcher()
    {
        var products = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase)
        {
            ["WC-STD-001"] = new("WC-STD-001", "Standard Wheelchair", "wheelchair"),
            ["OX-PORT-024"] = new("OX-PORT-024", "Portable Oxygen", "oxygen"),
            ["CP-STD-031"] = new("CP-STD-031", "Standard CPAP", "cpap"),
        };
        return new ProductMatcher(products);
    }

    [Fact]
    public void ExactMatch_ReturnsProduct()
    {
        var matcher = CreateMatcher();
        var (product, warning) = matcher.Match("WC-STD-001");
        product.Should().NotBeNull();
        product!.ProductCode.Should().Be("WC-STD-001");
        warning.Should().BeNull();
    }

    [Fact]
    public void CaseInsensitiveMatch_ReturnsProduct()
    {
        var matcher = CreateMatcher();
        var (product, warning) = matcher.Match("wc-std-001");
        product.Should().NotBeNull();
        product!.ProductCode.Should().Be("WC-STD-001");
        warning.Should().BeNull();
    }

    [Fact]
    public void FuzzyMatch_ReturnsProductWithWarning()
    {
        var matcher = CreateMatcher();
        // Missing a digit — close enough for fuzzy match
        var (product, warning) = matcher.Match("WC-STD-01");
        product.Should().NotBeNull();
        product!.ProductCode.Should().Be("WC-STD-001");
        warning.Should().Contain("matched to");
    }

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        var matcher = CreateMatcher();
        var (product, warning) = matcher.Match("ZZZZZ-999");
        product.Should().BeNull();
    }

    [Theory]
    [InlineData("abc", "abc", 0)]
    [InlineData("abc", "abd", 1)]
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    [InlineData("kitten", "sitting", 3)]
    public void LevenshteinDistance_IsCorrect(string a, string b, int expected)
    {
        ProductMatcher.LevenshteinDistance(a, b).Should().Be(expected);
    }
}
