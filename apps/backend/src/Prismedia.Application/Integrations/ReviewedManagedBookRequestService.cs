using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Reviews one Book work and accepts its selected manager-owned formats in one request.</summary>
public sealed class ReviewedManagedBookRequestService(
    ReviewedWantedBookService books,
    ManagedRequestService requests,
    IntegrationConnectionAccess access,
    IIntegrationManagerCreationGateway gateway,
    IManagedRequestStore requestStore,
    IReviewedManagedRequestCommitScope commitScope,
    ManagedLibraryService library,
    ExternalLibraryService externalLibraries,
    ILogger<ReviewedManagedBookRequestService> logger) {
    /// <summary>Checks each rendition's exact work identity and mapped library without persisting intent.</summary>
    public async Task<ReviewedManagedBookRequest> ReviewAsync(Guid connectionId,
        ReviewManagedBookRequestInput input, CancellationToken token) {
        Validate(input.EntityId, input.Request, input.Renditions);
        var authorized = await access.RequireAsync(connectionId, PluginCapability.ExternalManager,
            IntegrationOperation.LookupManaged, EntityKind.Book, token);
        var reviewed = new List<ReviewedManagedBookRendition>(input.Renditions.Count);
        var title = "";
        if (input.Request is { } request) {
            var work = ReviewedWantedBookService.ReviewWork(request);
            title = work.Title;
            var mounts = await externalLibraries.ListSuitableAsync(connectionId, EntityKind.Book, token);
            foreach (var choice in input.Renditions) {
                var identity = new ManagedLookupInput(EntityKind.Book,
                    new Dictionary<string, string> { [ExternalIdProviders.OpenLibraryWork] = work.WorkId },
                    BookRendition: choice.Rendition);
                var lookup = await gateway.LookupAsync(authorized.Manifest.Id, authorized.Context, identity, token);
                ManagedCreationEvidence.ValidateLookup(identity, lookup);
                var mount = mounts.SingleOrDefault(candidate => candidate.LibraryRootId == choice.LibraryRootId)
                    ?? throw new ArgumentException("Choose an enabled library mapped to this Book manager.");
                var options = await library.OptionsAsync(connectionId,
                    new(EntityKind.Book, choice.Rendition), token);
                ValidateRoot(options, mount);
                reviewed.Add(new(choice.Rendition, identity, mount, options, lookup.Existing));
            }
        } else {
            foreach (var choice in input.Renditions) {
                var preview = await requests.PreviewAsync(connectionId,
                    new(input.EntityId!.Value, choice.LibraryRootId, BookRendition: choice.Rendition), token);
                title = preview.Title;
                reviewed.Add(new(choice.Rendition, preview.Work, preview.Mount,
                    preview.Options, preview.Existing));
            }
        }
        return new(authorized.Connection.State.Revision, title, reviewed);
    }

    /// <summary>Ensures one Book, then accepts each format with a stable child operation and separate result.</summary>
    public async Task<CommitManagedBookRequestResponse> CommitAsync(Guid connectionId,
        CommitManagedBookRequestInput input, CancellationToken token) {
        if (input.OperationId == Guid.Empty)
            throw new ArgumentException("Supply a stable operation ID for this Book request.");
        var review = await ReviewAsync(connectionId,
            new(input.EntityId, input.Request, input.Renditions), token);
        if (review.ConnectionRevision != input.ExpectedConnectionRevision)
            throw new ConnectionConflictException("The Book manager changed. Review its libraries again.");
        var entityId = input.EntityId ?? (await books.PrepareAsync(input.Request!, token)).EntityId;
        var results = new List<ManagedBookRenditionResult>(input.Renditions.Count);
        foreach (var choice in input.Renditions) {
            var renditionReview = review.Renditions.Single(item => item.Rendition == choice.Rendition);
            var create = new CreateManagedRequestInput(OperationId(input.OperationId, choice.Rendition),
                entityId, choice.LibraryRootId, renditionReview.Work, ProfileId: null,
                Monitored: true, Search: choice.Search);
            try {
                var retained = (await requestStore.ListAsync(connectionId, token)).Where(item =>
                    item.Operation.State.EntityId == entityId
                    && item.Plan.Request.ReviewedWork.BookRendition == choice.Rendition
                    && item.Operation.State.Phase is not (ManagedRequestPhase.Cancelled or ManagedRequestPhase.OwnershipReleased)
                    && ManagedRequestIdentity.SameWork(item.Plan.Request.ReviewedWork, renditionReview.Work))
                    .ToArray();
                if (retained.Length > 0) {
                    if (retained.Length != 1 || retained[0].Operation.State.LibraryRootId != choice.LibraryRootId)
                        throw new ManagedRequestConflictException(
                            "This format already has a manager request in another mapped library.");
                    results.Add(new(choice.Rendition, ManagedRequestService.Map(retained[0])));
                    continue;
                }
                var accepted = await commitScope.ExecuteAsync(connectionId,
                    input.ExpectedConnectionRevision,
                    ct => requests.CreateAsync(connectionId, create, ct), token);
                results.Add(new(choice.Rendition, accepted));
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
                throw;
            } catch (Exception error) {
                logger.LogWarning(error, "Connected Book request failed for {BookId} and {Rendition}",
                    entityId, choice.Rendition);
                results.Add(new(choice.Rendition, Error:
                    "This format was not accepted. Review its manager and mapped library, then retry this request."));
            }
        }
        return new(entityId, results);
    }

    internal static void Validate(Guid? entityId, ReviewedRequestCommitRequest? request,
        IReadOnlyList<ManagedBookRenditionChoice>? renditions) {
        if ((entityId is { } id && id != Guid.Empty) == (request is not null)
            || renditions is null || renditions.Count is < 1 or > 2
            || renditions.Any(choice => !Enum.IsDefined(choice.Rendition) || choice.LibraryRootId == Guid.Empty)
            || renditions.Select(choice => choice.Rendition).Distinct().Count() != renditions.Count)
            throw new ArgumentException("Choose one Book work and each requested format once with a mapped library.");
        if (request is not null) ReviewedWantedBookService.ReviewWork(request);
    }

    private static void ValidateRoot(ManagerOptions options, ExternalLibraryMount mount) {
        if (!options.Roots.Any(root => root.Id == mount.RemoteRootId && root.Path == mount.RemotePath
            && root.Accessible != false))
            throw new ArgumentException("The mapped Book library changed or became inaccessible. Review its connection.");
    }

    private static Guid OperationId(Guid parent, BookRendition rendition) {
        Span<byte> seed = stackalloc byte[17];
        parent.TryWriteBytes(seed);
        seed[16] = checked((byte)rendition);
        return new Guid(SHA256.HashData(seed).AsSpan(0, 16));
    }
}
