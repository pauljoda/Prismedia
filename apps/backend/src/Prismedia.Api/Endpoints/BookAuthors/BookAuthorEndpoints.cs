using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class BookAuthorEndpoints {
    public static RouteGroupBuilder MapBookAuthorEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/book-authors",
            EntityKindRegistry.BookAuthor.Code,
            "Authors",
            "ListBookAuthors",
            "GetBookAuthor",
            typeof(EntityListResponse),
            typeof(BookAuthorDetail));
}
