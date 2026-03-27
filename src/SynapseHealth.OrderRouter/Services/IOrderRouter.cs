using SynapseHealth.OrderRouter.Models;

namespace SynapseHealth.OrderRouter.Services;

public interface IOrderRouter
{
    RouteResponse Route(OrderRequest order);
    BatchRouteResponse RouteBatch(List<OrderRequest> orders);
}
