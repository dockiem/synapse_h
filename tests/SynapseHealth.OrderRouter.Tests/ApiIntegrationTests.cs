using System.Net.Http.Json;
using SynapseHealth.OrderRouter.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SynapseHealth.OrderRouter.Tests;

public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task RouteEndpoint_AlwaysReturns200()
    {
        var order = new OrderRequest
        {
            OrderId = "TEST-INT-001",
            CustomerZip = "10015",
            Items = [new() { ProductCode = "WC-STD-001", Quantity = 1 }]
        };

        var response = await _client.PostAsJsonAsync("/api/route", order);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task RouteEndpoint_EmptyItems_ReturnsFeasibleFalse()
    {
        var order = new OrderRequest
        {
            OrderId = "TEST-INT-002",
            CustomerZip = "10015",
            Items = []
        };

        var response = await _client.PostAsJsonAsync("/api/route", order);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<RouteResponse>();
        result.Should().NotBeNull();
        result!.Feasible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("at least one line item"));
    }

    [Fact]
    public async Task RouteEndpoint_MissingZip_ReturnsFeasibleFalse()
    {
        var order = new OrderRequest
        {
            OrderId = "TEST-INT-003",
            Items = [new() { ProductCode = "WC-STD-001", Quantity = 1 }]
        };

        var response = await _client.PostAsJsonAsync("/api/route", order);
        var result = await response.Content.ReadFromJsonAsync<RouteResponse>();
        result!.Feasible.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("customer_zip"));
    }
}
