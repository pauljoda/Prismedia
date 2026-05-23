using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;

namespace Prismedia.Api.Endpoints;

public static class BookEndpoints {
    public static RouteGroupBuilder MapBookEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/books",
            "book",
            "Books",
            "ListBooks",
            "GetBook",
            typeof(EntityListResponse),
            typeof(BookDetail));
}
