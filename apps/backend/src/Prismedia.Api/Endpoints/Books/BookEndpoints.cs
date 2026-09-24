using Prismedia.Application.Books;
using Prismedia.Contracts.Books;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

/// <summary>Book routes: contents, chapter mappings, and the reading/listening alignment.</summary>
public static class BookEndpoints {
    #region Actions - Routes

    /// <summary>Maps the Book kind routes and the Book-specific contents, mapping, and alignment routes.</summary>
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

        group.MapGet("/{id:guid}/alignment", async (
            Guid id,
            BookAlignmentService alignments,
            CancellationToken cancellationToken) => {
                var result = await alignments.GetAsync(id, cancellationToken);
                return result is null
                    ? Results.NotFound(new ApiProblem(
                        ApiProblemCodes.EntityNotFound,
                        $"Book '{id}' was not found."))
                    : Results.Ok(result);
            })
            .WithName("GetBookAlignment")
            .WithSummary("Get the Book's reading/listening alignment and the current user's resume targets.")
            .Produces<BookAlignmentResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/chapter-mappings", async (
            Guid id,
            ReplaceBookChapterMappingsRequest request,
            IBookChapterMappingService mappings,
            BookAlignmentService alignments,
            CancellationToken cancellationToken) => {
                var result = await mappings.ReplaceAsync(id, request, cancellationToken);
                var alignment = result.Status == BookChapterMappingSaveStatus.Saved
                    ? await alignments.GetAsync(id, cancellationToken)
                    : null;
                return result.Status switch {
                    BookChapterMappingSaveStatus.Saved when alignment is not null => Results.Ok(alignment),
                    BookChapterMappingSaveStatus.Saved or BookChapterMappingSaveStatus.NotFound =>
                        Results.NotFound(new ApiProblem(
                            ApiProblemCodes.EntityNotFound,
                            $"Book '{id}' was not found.")),
                    BookChapterMappingSaveStatus.Invalid => Results.BadRequest(new ApiProblem(
                        ApiProblemCodes.InvalidBookChapterMapping,
                        result.Error ?? "The chapter mapping is invalid.")),
                    _ => throw new ArgumentOutOfRangeException(nameof(result.Status))
                };
            })
            .WithName("ReplaceBookChapterMappings")
            .WithSummary("Replace the Book's explicit audiobook-to-readable-chapter map and return the refreshed alignment.")
            .Produces<BookAlignmentResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    #endregion
}
