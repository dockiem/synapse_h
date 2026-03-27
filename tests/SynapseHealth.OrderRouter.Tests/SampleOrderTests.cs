using System.Net.Http.Json;
using SynapseHealth.OrderRouter.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SynapseHealth.OrderRouter.Tests;

public class SampleOrderTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SampleOrderTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ORD001_NYC_WheelchairAndOxygen_RoutesSuccessfully()
    {
        var order = new OrderRequest
        {
            OrderId = "ORD-001",
            CustomerZip = "10015",
            MailOrder = false,
            Items =
            [
                new() { ProductCode = "WC-STD-001", Quantity = 1 },
                new() { ProductCode = "OX-PORT-024", Quantity = 1 }
            ]
        };

        var response = await _client.PostAsJsonAsync("/api/route", order);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<RouteResponse>();
        result!.Feasible.Should().BeTrue();
        result.Routing.Should().NotBeEmpty();

        // All items should be assigned
        var allItems = result.Routing!.SelectMany(r => r.Items).ToList();
        allItems.Should().HaveCount(2);
        allItems.Should().Contain(i => i.ProductCode == "WC-STD-001");
        allItems.Should().Contain(i => i.ProductCode == "OX-PORT-024");

        // Local fulfillment since mail_order=false
        allItems.Should().OnlyContain(i => i.FulfillmentMode == "local");
    }

    [Fact]
    public async Task ORD002_Houston_MultiCategory_RoutesSuccessfully()
    {
        var order = new OrderRequest
        {
            OrderId = "ORD-002",
            CustomerZip = "77059",
            MailOrder = false,
            Items =
            [
                new() { ProductCode = "HB-FUL-018", Quantity = 1 },
                new() { ProductCode = "PL-ELEC-043", Quantity = 1 },
                new() { ProductCode = "CM-BED-048", Quantity = 1 },
                new() { ProductCode = "BP-AUTO-077", Quantity = 1 }
            ]
        };

        var response = await _client.PostAsJsonAsync("/api/route", order);
        var result = await response.Content.ReadFromJsonAsync<RouteResponse>();
        result!.Feasible.Should().BeTrue();

        var allItems = result.Routing!.SelectMany(r => r.Items).ToList();
        allItems.Should().HaveCount(4);
    }

    [Fact]
    public async Task ORD003_Boston_MailOrder_Respiratory_RoutesSuccessfully()
    {
        // This is the critical test: Boston ZIP 02130 must match suppliers
        // storing ZIPs as "2130" (without leading zero)
        var order = new OrderRequest
        {
            OrderId = "ORD-003",
            CustomerZip = "02130",
            MailOrder = true,
            Items =
            [
                new() { ProductCode = "CP-STD-031", Quantity = 1 },
                new() { ProductCode = "CP-MSK-FF-035", Quantity = 2 },
                new() { ProductCode = "NB-COMP-039", Quantity = 1 }
            ]
        };

        var response = await _client.PostAsJsonAsync("/api/route", order);
        var result = await response.Content.ReadFromJsonAsync<RouteResponse>();
        result!.Feasible.Should().BeTrue();

        var allItems = result.Routing!.SelectMany(r => r.Items).ToList();
        allItems.Should().HaveCount(3);

        // Should match respiratory categories
        allItems.Should().Contain(i => i.Category == "cpap");
        allItems.Should().Contain(i => i.Category == "nebulizer");
    }
}
