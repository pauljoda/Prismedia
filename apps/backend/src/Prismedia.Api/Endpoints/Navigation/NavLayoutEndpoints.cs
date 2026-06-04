using Prismedia.Application.Navigation;
using Prismedia.Contracts.Navigation;

namespace Prismedia.Api.Endpoints;

public static class NavLayoutEndpoints {
    public static RouteGroupBuilder MapNavLayoutEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/nav-layout")
            .WithTags("Navigation");

        group.MapGet("/", async (
            NavLayoutService layout,
            CancellationToken cancellationToken) =>
            new NavLayoutResponse(await layout.GetAsync(cancellationToken)))
            .WithName("GetNavLayout")
            .WithSummary("Gets the user's server-persisted navigation layout.")
            .Produces<NavLayoutResponse>();

        group.MapPut("/", async (
            NavLayoutDocument request,
            NavLayoutService layout,
            CancellationToken cancellationToken) =>
            new NavLayoutResponse(await layout.SaveAsync(request, cancellationToken)))
            .WithName("UpdateNavLayout")
            .WithSummary("Saves the user's navigation layout, shared across devices.")
            .Produces<NavLayoutResponse>();

        return group;
    }
}
