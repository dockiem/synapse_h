// Entry point for the Order Router API. Loads supplier/product CSVs at startup,
// registers a single POST endpoint that accepts both individual and batch orders,
// and routes them to the best supplier(s) based on ZIP coverage and product catalog.

using SynapseHealth.OrderRouter.Data;
using SynapseHealth.OrderRouter.Models;
using SynapseHealth.OrderRouter.Services;

var builder = WebApplication.CreateBuilder(args);

// Use port 8080 consistently (local, Docker, scripts)
builder.WebHost.UseUrls("http://0.0.0.0:8080");

// Determine data directory by searching multiple candidate paths
var dataDir = ResolveDataDir()
    ?? throw new FileNotFoundException("Cannot locate data directory with suppliers.csv");

static string? ResolveDataDir()
{
    // Explicit candidates: Docker, relative to binary, current working dir
    var candidates = new List<string> { "/app/data", Path.Combine(AppContext.BaseDirectory, "data"), "data" };

    // Walk up from the base directory to find repo root's data/ folder
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        candidates.Add(Path.Combine(dir.FullName, "data"));
        dir = dir.Parent;
    }

    return candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "suppliers.csv")));
}

builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILoggerFactory>();
    var suppliers = DataLoader.LoadSuppliersAsync(
        Path.Combine(dataDir, "suppliers.csv"),
        logger.CreateLogger("DataLoader")).GetAwaiter().GetResult();
    return suppliers;
});

builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILoggerFactory>();
    var products = DataLoader.LoadProductsAsync(
        Path.Combine(dataDir, "products.csv"),
        logger.CreateLogger("DataLoader")).GetAwaiter().GetResult();
    return products;
});

builder.Services.AddSingleton<IOrderRouter>(sp =>
{
    var suppliers = sp.GetRequiredService<List<Supplier>>();
    var products = sp.GetRequiredService<Dictionary<string, Product>>();
    var logger = sp.GetRequiredService<ILogger<OrderRouterService>>();
    return new OrderRouterService(suppliers, products, logger);
});

var app = builder.Build();

// Serve the upload UI at /
app.UseDefaultFiles();
app.UseStaticFiles();

// Eagerly resolve data singletons at startup so CSV parse errors surface
// immediately rather than on the first request
app.Services.GetRequiredService<List<Supplier>>();
app.Services.GetRequiredService<Dictionary<string, Product>>();
app.Logger.LogInformation("Data loaded from: {DataDir}", Path.GetFullPath(dataDir));

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/api/route", async (HttpRequest request, IOrderRouter router) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    // Peek at the first non-whitespace char to distinguish single vs. batch requests
    // on the same endpoint, avoiding the need for separate routes.
    var trimmed = body.TrimStart();

    if (trimmed.StartsWith('['))
    {
        var orders = System.Text.Json.JsonSerializer.Deserialize<List<OrderRequest>>(body);
        if (orders == null || orders.Count == 0)
            return Results.Ok(RouteResponse.Failure(["Request body is an empty array."]));

        var response = router.RouteBatch(orders);
        return Results.Ok(response);
    }
    else
    {
        // Single order
        var order = System.Text.Json.JsonSerializer.Deserialize<OrderRequest>(body);
        if (order == null)
            return Results.Ok(RouteResponse.Failure(["Invalid order JSON."]));

        var response = router.Route(order);
        return Results.Ok(response);
    }
});

app.Run();

// Partial class declaration required for WebApplicationFactory<Program> in integration tests
public partial class Program;
