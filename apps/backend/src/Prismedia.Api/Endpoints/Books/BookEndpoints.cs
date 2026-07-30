using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class BookEndpoints {
    public static RouteGroupBuilder MapBookEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/books",
            EntityKind.Book.ToCode(),
            "Books",
            "ListBooks",
            "GetBook");
}
