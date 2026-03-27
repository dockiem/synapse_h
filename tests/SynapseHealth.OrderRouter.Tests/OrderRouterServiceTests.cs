using SynapseHealth.OrderRouter.Models;
using SynapseHealth.OrderRouter.Services;
using SynapseHealth.OrderRouter.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace SynapseHealth.OrderRouter.Tests;

public class OrderRouterServiceTests
{
    private static OrderRouterService CreateRouter(List<Supplier> suppliers, Dictionary<string, Product>? products = null)
    {
        products ??= new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase)
        {
            ["WC-STD-001"] = new("WC-STD-001", "Standard Wheelchair", "wheelchair"),
            ["OX-PORT-024"] = new("OX-PORT-024", "Portable Oxygen", "oxygen"),
            ["CN-STD-060"] = new("CN-STD-060", "Standard Cane", "cane"),
        };

        return new OrderRouterService(suppliers, products, NullLogger<OrderRouterService>.Instance);
    }

    private static Supplier MakeSupplier(string id, string name, string zips, string[] categories, double? score, bool canMail = false) =>
        new()
        {
            SupplierId = id,
            SupplierName = name,
            ZipCoverage = ZipCoverage.Parse(zips),
            ProductCategories = categories.Select(c => c.ToLowerInvariant()).ToHashSet(),
            SatisfactionScore = score,
            CanMailOrder = canMail
        };

    [Fact]
    public void EmptyItems_ReturnsFeasibleFalse()
    {
        var router = CreateRouter([]);
        var result = router.Route(new OrderRequest { CustomerZip = "10015", Items = [] });
        result.Feasible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("at least one line item"));
    }

    [Fact]
    public void MissingZip_ReturnsFeasibleFalse()
    {
        var router = CreateRouter([]);
        var result = router.Route(new OrderRequest
        {
            Items = [new() { ProductCode = "WC-STD-001", Quantity = 1 }]
        });
        result.Feasible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("customer_zip"));
    }

    [Fact]
    public void SingleSupplier_CoversAllItems_Consolidates()
    {
        var supplier = MakeSupplier("SUP-001", "Full Service", "10001-10100",
            ["wheelchair", "oxygen"], 8.0);

        var router = CreateRouter([supplier]);
        var result = router.Route(new OrderRequest
        {
            OrderId = "TEST-001",
            CustomerZip = "10015",
            Items =
            [
                new() { ProductCode = "WC-STD-001", Quantity = 1 },
                new() { ProductCode = "OX-PORT-024", Quantity = 1 }
            ]
        });

        result.Feasible.Should().BeTrue();
        result.Routing.Should().HaveCount(1);
        result.Routing![0].SupplierId.Should().Be("SUP-001");
        result.Routing[0].Items.Should().HaveCount(2);
    }

    [Fact]
    public void PrefersConsolidation_OverHigherRatedSpecialists()
    {
        var generalist = MakeSupplier("SUP-GEN", "Generalist", "10001-10100",
            ["wheelchair", "oxygen"], 6.0);
        var specialist1 = MakeSupplier("SUP-WC", "Wheelchair Specialist", "10001-10100",
            ["wheelchair"], 10.0);
        var specialist2 = MakeSupplier("SUP-OX", "Oxygen Specialist", "10001-10100",
            ["oxygen"], 10.0);

        var router = CreateRouter([generalist, specialist1, specialist2]);
        var result = router.Route(new OrderRequest
        {
            OrderId = "TEST-002",
            CustomerZip = "10015",
            Items =
            [
                new() { ProductCode = "WC-STD-001", Quantity = 1 },
                new() { ProductCode = "OX-PORT-024", Quantity = 1 }
            ]
        });

        result.Feasible.Should().BeTrue();
        // Generalist covers both items — consolidation (2*10 + 6 + 0.5 = 26.5) beats
        // specialist covering 1 item (1*10 + 10 + 0.5 = 20.5)
        result.Routing.Should().HaveCount(1);
        result.Routing![0].SupplierId.Should().Be("SUP-GEN");
    }

    [Fact]
    public void PrefersHigherRated_WhenSameCoverage()
    {
        var low = MakeSupplier("SUP-LOW", "Low Rated", "10001-10100", ["wheelchair"], 3.0);
        var high = MakeSupplier("SUP-HIGH", "High Rated", "10001-10100", ["wheelchair"], 9.0);

        var router = CreateRouter([low, high]);
        var result = router.Route(new OrderRequest
        {
            OrderId = "TEST-003",
            CustomerZip = "10015",
            Items = [new() { ProductCode = "WC-STD-001", Quantity = 1 }]
        });

        result.Feasible.Should().BeTrue();
        result.Routing![0].SupplierId.Should().Be("SUP-HIGH");
    }

    [Fact]
    public void PrefersLocal_OverMailOrder()
    {
        var local = MakeSupplier("SUP-LOCAL", "Local Co", "10001-10100", ["wheelchair"], 8.0);
        var mail = MakeSupplier("SUP-MAIL", "Mail Co", "90001-90100", ["wheelchair"], 8.0, canMail: true);

        var router = CreateRouter([local, mail]);
        var result = router.Route(new OrderRequest
        {
            OrderId = "TEST-004",
            CustomerZip = "10015",
            MailOrder = true,
            Items = [new() { ProductCode = "WC-STD-001", Quantity = 1 }]
        });

        result.Feasible.Should().BeTrue();
        result.Routing![0].SupplierId.Should().Be("SUP-LOCAL");
        result.Routing[0].Items[0].FulfillmentMode.Should().Be("local");
    }

    [Fact]
    public void PrefersHigherRatedMailOrder_OverLowerRatedLocal()
    {
        // Per spec: quality (#3) beats geographic preference (#4).
        // Even a slight satisfaction edge should win over local preference.
        var local = MakeSupplier("SUP-LOCAL", "Local Co", "10001-10100", ["wheelchair"], 7.9);
        var mail = MakeSupplier("SUP-MAIL", "Mail Co", "90001-90100", ["wheelchair"], 8.0, canMail: true);

        var router = CreateRouter([local, mail]);
        var result = router.Route(new OrderRequest
        {
            OrderId = "TEST-QUALITY-WINS",
            CustomerZip = "10015",
            MailOrder = true,
            Items = [new() { ProductCode = "WC-STD-001", Quantity = 1 }]
        });

        result.Feasible.Should().BeTrue();
        // Mail-order supplier wins because 8.0 > 7.9, even though local is available
        result.Routing![0].SupplierId.Should().Be("SUP-MAIL");
        result.Routing[0].Items[0].FulfillmentMode.Should().Be("mail_order");
    }

    [Fact]
    public void PrefersLocal_WhenRatingsIdentical()
    {
        // Per spec: "prefer local over mail-order when ratings are similar"
        // With identical scores, local wins as the tiebreaker.
        var local = MakeSupplier("SUP-LOCAL", "Local Co", "10001-10100", ["wheelchair"], 8.0);
        var mail = MakeSupplier("SUP-MAIL", "Mail Co", "90001-90100", ["wheelchair"], 8.0, canMail: true);

        var router = CreateRouter([local, mail]);
        var result = router.Route(new OrderRequest
        {
            OrderId = "TEST-LOCAL-TIEBREAK",
            CustomerZip = "10015",
            MailOrder = true,
            Items = [new() { ProductCode = "WC-STD-001", Quantity = 1 }]
        });

        result.Feasible.Should().BeTrue();
        result.Routing![0].SupplierId.Should().Be("SUP-LOCAL");
        result.Routing[0].Items[0].FulfillmentMode.Should().Be("local");
    }

    [Fact]
    public void MailOrder_ExpandsEligibleSuppliers()
    {
        var remote = MakeSupplier("SUP-REMOTE", "Remote Co", "90001-90100", ["wheelchair"], 9.0, canMail: true);

        var router = CreateRouter([remote]);
        // Without mail_order, ZIP 10015 would not match
        var noMail = router.Route(new OrderRequest
        {
            CustomerZip = "10015",
            MailOrder = false,
            Items = [new() { ProductCode = "WC-STD-001", Quantity = 1 }]
        });
        noMail.Feasible.Should().BeFalse();

        // With mail_order, remote supplier is eligible
        var withMail = router.Route(new OrderRequest
        {
            CustomerZip = "10015",
            MailOrder = true,
            Items = [new() { ProductCode = "WC-STD-001", Quantity = 1 }]
        });
        withMail.Feasible.Should().BeTrue();
        withMail.Routing![0].Items[0].FulfillmentMode.Should().Be("mail_order");
    }

    [Fact]
    public void NoSupplierAvailable_ReturnsFeasibleFalse()
    {
        var supplier = MakeSupplier("SUP-001", "NYC Only", "10001-10100", ["wheelchair"], 8.0);

        var router = CreateRouter([supplier]);
        var result = router.Route(new OrderRequest
        {
            CustomerZip = "90210",
            Items = [new() { ProductCode = "WC-STD-001", Quantity = 1 }]
        });

        result.Feasible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("No supplier available"));
    }

    [Fact]
    public void UnratedSupplier_UsesDefaultScore()
    {
        // Unrated supplier should still be eligible, using default score of 5.0
        var unrated = MakeSupplier("SUP-UNRATED", "Unrated Co", "10001-10100", ["wheelchair"], null);

        var router = CreateRouter([unrated]);
        var result = router.Route(new OrderRequest
        {
            CustomerZip = "10015",
            Items = [new() { ProductCode = "WC-STD-001", Quantity = 1 }]
        });

        result.Feasible.Should().BeTrue();
        result.Routing![0].SupplierId.Should().Be("SUP-UNRATED");
    }
}
