using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Taxonomy;

namespace Prismedia.Api.Endpoints;

public static class TagEndpoints {
    public static RouteGroupBuilder MapTagEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/tags",
            "tag",
            "Taxonomy",
            "ListTags",
            "GetTag",
            typeof(EntityListResponse),
            typeof(TagDetail));
}
