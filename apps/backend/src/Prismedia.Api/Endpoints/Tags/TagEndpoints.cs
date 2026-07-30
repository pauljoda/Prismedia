using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class TagEndpoints {
    public static RouteGroupBuilder MapTagEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/tags",
            EntityKind.Tag.ToCode(),
            "Taxonomy",
            "ListTags",
            "GetTag",
            manageable: true);
}
