using System.Net.Http.Json;
using System.Text;
using SynapseHealth.OrderRouter.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SynapseHealth.OrderRouter.Tests;

public class BatchRouteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public BatchRouteTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BatchRoute_AllValid_ReturnsAllProcessed()
    {
        var orders = new[]
        {
            new OrderRequest
            {
                OrderId = "BATCH-001",
                CustomerZip = "10015",
                Items = [new() { ProductCode = "WC-STD-001", Quantity = 1 }]
            },
            new OrderRequest
            {
                OrderId = "BATCH-002",
                CustomerZip = "10015",
                Items = [new() { ProductCode = "OX-PORT-024", Quantity = 1 }]
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(orders);
        var response = await _client.PostAsync("/api/route",
            new StringContent(json, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchRouteResponse>();
        result.Should().NotBeNull();
        result!.Processed.Should().Be(2);
        result.Failed.Should().Be(0);
        result.Results.Should().HaveCount(2);
        result.Results.Should().OnlyContain(r => r.Feasible);
        result.DeadLetter.Should().BeNull();
    }

    [Fact]
    public async Task BatchRoute_MixedValidAndInvalid_ReturnsPartialResults()
    {
        var orders = new[]
        {
            new OrderRequest
            {
                OrderId = "BATCH-OK",
                CustomerZip = "10015",
                Items = [new() { ProductCode = "WC-STD-001", Quantity = 1 }]
            },
            new OrderRequest
            {
                OrderId = "BATCH-BAD",
                CustomerZip = "10015",
                Items = [new() { ProductCode = "NONEXISTENT-999", Quantity = 1 }]
            },
            new OrderRequest
            {
                OrderId = "BATCH-OK2",
                CustomerZip = "10015",
                Items = [new() { ProductCode = "OX-PORT-024", Quantity = 1 }]
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(orders);
        var response = await _client.PostAsync("/api/route",
            new StringContent(json, Encoding.UTF8, "application/json"));

        var result = await response.Content.ReadFromJsonAsync<BatchRouteResponse>();
        result!.Processed.Should().Be(2);
        result.Failed.Should().Be(1);
        result.Results.Should().HaveCount(3);

        // Successful orders still routed
        result.Results.Where(r => r.Feasible).Should().HaveCount(2);

        // Failed order captured in dead letter
        result.DeadLetter.Should().HaveCount(1);
        result.DeadLetter![0].Order.OrderId.Should().Be("BATCH-BAD");
        result.DeadLetter[0].Errors.Should().Contain(e => e.Contains("NONEXISTENT-999"));
    }

    [Fact]
    public async Task BatchRoute_AllInvalid_AllGoToDeadLetter()
    {
        var orders = new[]
        {
            new OrderRequest { OrderId = "BAD-1", Items = [] },
            new OrderRequest { OrderId = "BAD-2", CustomerZip = "10015" }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(orders);
        var response = await _client.PostAsync("/api/route",
            new StringContent(json, Encoding.UTF8, "application/json"));

        var result = await response.Content.ReadFromJsonAsync<BatchRouteResponse>();
        result!.Processed.Should().Be(0);
        result.Failed.Should().Be(2);
        result.DeadLetter.Should().HaveCount(2);
        result.DeadLetterFile.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SingleOrder_StillWorksAsObject()
    {
        // Backward compatibility: single order object (not array)
        var order = new OrderRequest
        {
            OrderId = "SINGLE-001",
            CustomerZip = "10015",
            Items = [new() { ProductCode = "WC-STD-001", Quantity = 1 }]
        };

        var response = await _client.PostAsJsonAsync("/api/route", order);
        var result = await response.Content.ReadFromJsonAsync<RouteResponse>();
        result!.Feasible.Should().BeTrue();
        result.OrderId.Should().Be("SINGLE-001");
    }

    [Fact]
    public async Task BatchRoute_SendsSampleOrdersFile_AllRoute()
    {
        // Simulate sending the entire sample_orders.json array
        var orders = new[]
        {
            new OrderRequest
            {
                OrderId = "ORD-001",
                CustomerZip = "10015",
                MailOrder = false,
                Items =
                [
                    new() { ProductCode = "WC-STD-001", Quantity = 1 },
                    new() { ProductCode = "OX-PORT-024", Quantity = 1 }
                ]
            },
            new OrderRequest
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
            },
            new OrderRequest
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
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(orders);
        var response = await _client.PostAsync("/api/route",
            new StringContent(json, Encoding.UTF8, "application/json"));

        var result = await response.Content.ReadFromJsonAsync<BatchRouteResponse>();
        result!.Processed.Should().Be(3);
        result.Failed.Should().Be(0);
        result.DeadLetter.Should().BeNull();
    }
}
