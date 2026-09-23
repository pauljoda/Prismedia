using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Prismedia.Application.Requests;

public sealed partial class RequestCommitService {
    /// <summary>Commits one reviewed Book work and the selected formats without a second provider review.</summary>
    private async Task<RequestCommitResponse> CommitReviewedBookRenditionsAsync(
        ReviewedRequestCommitRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (request.Kind is not (RequestMediaKind.Book or RequestMediaKind.Audiobook)
            || request.Review?.EntityKind != EntityKind.Book
            || request.Proposal?.TargetKind != EntityKind.Book
            || request.SelectedProposalIds?.Count != 1
            || request.SelectedProposalIds[0] != request.Review.Proposal.ProposalId) {
            throw new RequestCommitValidationException("Choose one Book work from a complete review before requesting its formats.");
        }
        ValidateRenditions(request.BookRenditions);

        var results = new List<BookRenditionCommitResult>();
        var items = new List<RequestCommitItem>();
        Guid? bookId = null;
        foreach (var choice in request.BookRenditions!) {
            var kind = choice.Rendition == BookRendition.Ebook
                ? RequestMediaKind.Book
                : RequestMediaKind.Audiobook;
            var rootTarget = request.Review.Targets.SingleOrDefault(target =>
                target.ProposalId == request.Review.Proposal.ProposalId);
            if (rootTarget is null) {
                throw new RequestCommitValidationException("The reviewed Book has no requestable root target.");
            }
            var single = request with {
                Kind = kind,
                Review = request.Review with {
                    Kind = kind,
                    Targets = request.Review.Targets.Select(target =>
                        target.ProposalId == rootTarget.ProposalId ? target with { Kind = kind } : target).ToArray()
                },
                TargetLibraryRootId = choice.TargetLibraryRootId ?? request.TargetLibraryRootId,
                ProfileId = choice.ProfileId ?? request.ProfileId,
                BookRenditions = null
            };
            try {
                RequestCommitResponse? committed;
                if (bookId is { } existingBookId) {
                    var descriptor = RequestKindRegistry.Find(kind)!;
                    var targeting = await ResolveInteractiveTargetingAsync(
                        descriptor,
                        new AcquisitionTargeting(single.TargetLibraryRootId, single.ProfileId),
                        hideNsfw,
                        cancellationToken);
                    committed = await RequestEntityFromGraphAsync(
                        existingBookId, hideNsfw, cancellationToken, targeting, choice.Rendition);
                } else {
                    committed = await CommitReviewedAsync(single, hideNsfw, cancellationToken);
                }
                var item = committed?.Items.SingleOrDefault();
                if (item is null) {
                    results.Add(new BookRenditionCommitResult(
                        choice.Rendition, null, "This format could not be requested. Retry it from the Book page."));
                    continue;
                }
                bookId = item.EntityId;
                items.Add(item);
                results.Add(new BookRenditionCommitResult(choice.Rendition, item));
            } catch (RequestCommitValidationException) when (results.Count == 0) {
                throw;
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception error) {
                logger?.LogError(error, "Reviewed Book rendition request failed for {Identity} and {Rendition}",
                    request.RootExternalIdentity, choice.Rendition);
                results.Add(new BookRenditionCommitResult(
                    choice.Rendition, null, "This format could not be requested. Retry it from the Book page."));
            }
        }
        return new RequestCommitResponse(null, items, BookRenditions: results);
    }

    /// <summary>
    /// Requests the selected formats of one existing Book. Each rendition uses the ordinary
    /// deduplication and targeting path; a failure in one does not discard its sibling's outcome.
    /// </summary>
    public async Task<RequestCommitResponse?> RequestBookRenditionsAsync(
        RequestBookRenditionsCommitRequest request,
        bool hideNsfw,
        CancellationToken cancellationToken) {
        if (request.EntityId == Guid.Empty) {
            throw new RequestCommitValidationException("A Book is required.");
        }
        ValidateRenditions(request.Renditions);

        var entity = await wanted.GetEntityAsync(request.EntityId, cancellationToken);
        if (entity?.Kind != EntityKind.Book) {
            return null;
        }

        var results = new List<BookRenditionCommitResult>(request.Renditions.Count);
        var items = new List<RequestCommitItem>(request.Renditions.Count);
        foreach (var choice in request.Renditions) {
            try {
                var response = await RequestEntityAsync(
                    request.EntityId,
                    hideNsfw,
                    cancellationToken,
                    new AcquisitionTargeting(choice.TargetLibraryRootId, choice.ProfileId),
                    choice.Rendition);
                var item = response?.Items.SingleOrDefault();
                if (item is null) {
                    results.Add(new BookRenditionCommitResult(
                        choice.Rendition, null, "This format could not be requested. Retry it from the Book page."));
                    continue;
                }
                items.Add(item);
                results.Add(new BookRenditionCommitResult(choice.Rendition, item));
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception error) {
                logger?.LogError(error, "Book rendition request failed for {BookId} and {Rendition}",
                    request.EntityId, choice.Rendition);
                results.Add(new BookRenditionCommitResult(
                    choice.Rendition, null, "This format could not be requested. Retry it from the Book page."));
            }
        }

        return new RequestCommitResponse(null, items, BookRenditions: results);
    }

    private static void ValidateRenditions(IReadOnlyList<BookRenditionRequestChoice>? choices) {
        if (choices is null || choices.Count is < 1 or > 2
            || choices.Any(choice => !Enum.IsDefined(choice.Rendition))
            || choices.Select(choice => choice.Rendition).Distinct().Count() != choices.Count) {
            throw new RequestCommitValidationException("Choose ebook, audiobook, or both once.");
        }
    }
}
