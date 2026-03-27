namespace SynapseHealth.OrderRouter.Models;

public sealed record Product(
    string ProductCode,
    string ProductName,
    string Category
);
