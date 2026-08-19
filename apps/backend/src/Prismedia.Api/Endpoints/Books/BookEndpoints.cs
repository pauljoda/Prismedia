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

        group.MapGet("/{id:guid}/chapter-mappings", async (
            Guid id,
            IBookChapterMappingService mappings,
            CancellationToken cancellationToken) => {
                var result = await mappings.GetAsync(id, cancellationToken);
                return result is null
                    ? Results.NotFound(new ApiProblem(
                        ApiProblemCodes.EntityNotFound,
                        $"Book '{id}' was not found."))
                    : Results.Ok(result);
            })
            .WithName("GetBookChapterMappings")
            .WithSummary("Get the Book's explicit audiobook-to-readable-chapter map.")
            .Produces<BookChapterMappingsResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/chapter-mappings", async (
            Guid id,
            ReplaceBookChapterMappingsRequest request,
            IBookChapterMappingService mappings,
            CancellationToken cancellationToken) => {
                var result = await mappings.ReplaceAsync(id, request, cancellationToken);
                return result.Status switch {
                    BookChapterMappingSaveStatus.Saved => Results.Ok(result.Response),
                    BookChapterMappingSaveStatus.NotFound => Results.NotFound(new ApiProblem(
                        ApiProblemCodes.EntityNotFound,
                        $"Book '{id}' was not found.")),
                    BookChapterMappingSaveStatus.Invalid => Results.BadRequest(new ApiProblem(
                        ApiProblemCodes.InvalidBookChapterMapping,
                        result.Error ?? "The chapter mapping is invalid.")),
                    _ => throw new ArgumentOutOfRangeException(nameof(result.Status))
                };
            })
            .WithName("ReplaceBookChapterMappings")
            .WithSummary("Replace the Book's explicit audiobook-to-readable-chapter map.")
            .Produces<BookChapterMappingsResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }
}
