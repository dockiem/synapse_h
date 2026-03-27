using SynapseHealth.OrderRouter.Utils;
using FluentAssertions;

namespace SynapseHealth.OrderRouter.Tests;

public class ZipCoverageTests
{
    [Fact]
    public void NormalizeZip_PadsLeadingZeros()
    {
        ZipCoverage.NormalizeZip("2130").Should().Be("02130");
        ZipCoverage.NormalizeZip("123").Should().Be("00123");
    }

    [Fact]
    public void NormalizeZip_KeepsFiveDigitZip()
    {
        ZipCoverage.NormalizeZip("10015").Should().Be("10015");
    }

    [Fact]
    public void NormalizeZip_StripsTrailingQuotes()
    {
        ZipCoverage.NormalizeZip("10046\"").Should().Be("10046");
    }

    [Fact]
    public void Parse_ExplicitList()
    {
        var zc = ZipCoverage.Parse("10001, 10002, 10003");
        zc.Covers("10001").Should().BeTrue();
        zc.Covers("10002").Should().BeTrue();
        zc.Covers("10003").Should().BeTrue();
        zc.Covers("10004").Should().BeFalse();
        zc.IsNationwide.Should().BeFalse();
    }

    [Fact]
    public void Parse_SingleRange()
    {
        var zc = ZipCoverage.Parse("10255-10275");
        zc.Covers("10255").Should().BeTrue();
        zc.Covers("10265").Should().BeTrue();
        zc.Covers("10275").Should().BeTrue();
        zc.Covers("10254").Should().BeFalse();
        zc.Covers("10276").Should().BeFalse();
    }

    [Fact]
    public void Parse_Nationwide()
    {
        var zc = ZipCoverage.Parse("00100-99999");
        zc.IsNationwide.Should().BeTrue();
        zc.Covers("10015").Should().BeTrue();
        zc.Covers("90210").Should().BeTrue();
        zc.Covers("02130").Should().BeTrue();
    }

    [Fact]
    public void Parse_MixedRanges()
    {
        var zc = ZipCoverage.Parse("2164-2213, 2143-2193, 2154-2171");
        // Boston ZIPs with leading zero padding
        zc.Covers("02164").Should().BeTrue();
        zc.Covers("02193").Should().BeTrue();
        zc.Covers("02150").Should().BeTrue();
        zc.Covers("02214").Should().BeFalse();
    }

    [Fact]
    public void Parse_BostonLeadingZero_MatchesCustomerZip()
    {
        // Supplier has "2130" stored without leading zero
        var zc = ZipCoverage.Parse("2130");
        // Customer order has "02130" with leading zero
        zc.Covers("02130").Should().BeTrue();
    }

    [Fact]
    public void Parse_TrailingQuoteInData()
    {
        var zc = ZipCoverage.Parse("10459-10474\"");
        zc.Covers("10460").Should().BeTrue();
        zc.Covers("10474").Should().BeTrue();
    }

    [Fact]
    public void Parse_EmptyOrNull_ReturnsEmptyCoverage()
    {
        ZipCoverage.Parse(null).Covers("10001").Should().BeFalse();
        ZipCoverage.Parse("").Covers("10001").Should().BeFalse();
        ZipCoverage.Parse("  ").Covers("10001").Should().BeFalse();
    }

    [Fact]
    public void Parse_MixedExplicitAndRange()
    {
        var zc = ZipCoverage.Parse("77176-77209, 77216, 77075");
        zc.Covers("77180").Should().BeTrue();
        zc.Covers("77216").Should().BeTrue();
        zc.Covers("77075").Should().BeTrue();
        zc.Covers("77210").Should().BeFalse();
    }
}
