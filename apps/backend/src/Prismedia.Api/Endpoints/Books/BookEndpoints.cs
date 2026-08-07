using Prismedia.Application.Books;
using Prismedia.Contracts.Books;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class BookEndpoints {
    public static RouteGroupBuilder MapBookEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapEntityKindRoutes(
            "/api/books",
            EntityKind.Book.ToCode(),
            "Books",
            "ListBooks",
            "GetBook");

        group.MapGet("/{id:guid}/contents", async (
            Guid id,
            IBookContentsService contents,
            CancellationToken cancellationToken) => {
                var result = await contents.GetAsync(id, cancellationToken);
                return result is null
                    ? Results.NotFound(new ApiProblem(
                        ApiProblemCodes.EntityNotFound,
                        $"Readable EPUB contents for book '{id}' were not found."))
                    : Results.Ok(result);
            })
            .WithName("GetBookContents")
            .WithSummary("Get compact EPUB contents and reading-order ranges.")
            .Produces<BookContentsResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }
}
