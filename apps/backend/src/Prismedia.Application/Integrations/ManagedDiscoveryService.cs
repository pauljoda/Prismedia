using System.Globalization;
using Prismedia.Application.Requests;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Plugins;
using Prismedia.Contracts.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Searches and reviews one selected external manager's upstream catalog without remote mutation.</summary>
public sealed class ManagedDiscoveryService(
    IntegrationConnectionAccess access,
    IIntegrationManagerGateway discovery,
    IIntegrationManagerCreationGateway lookup,
    ReviewedWantedMovieService wanted) {
    private const string DiscoveryMatchReason = "Connected catalog";
    /// <summary>Returns bounded manager candidates with server-selected persistent identities.</summary>
    public async Task<ManagedDiscoverySearchResponse> SearchAsync(
        Guid connectionId,
        ManagedDiscoveryQuery input,
        CancellationToken token) {
        ValidateSearch(input);
        var connection = await access.RequireAsync(
            connectionId,
            PluginCapability.ExternalManager,
            IntegrationOperation.DiscoverManaged,
            input.EntityKind,
            token);
        var page = await discovery.DiscoverAsync(connection.Manifest.Id, connection.Context, input, token);
        if (page?.Items is null || page.Items.Count > input.Limit) throw InvalidEvidence();

        var results = new List<ManagedDiscoverySearchResult>(page.Items.Count);
        var identities = new HashSet<ExternalIdentity>();
        foreach (var candidate in page.Items) {
            ValidateCandidate(candidate, input.EntityKind);
            var identity = CanonicalIdentity(candidate.EntityKind, candidate.ExternalIds);
            if (!identities.Add(identity)) throw InvalidEvidence();
            results.Add(new(candidate.EntityKind, candidate.Title, candidate.Year, identity, candidate.Metadata));
        }
        return new(results);
    }

    /// <summary>Builds the shared metadata review shape from a fresh exact manager lookup.</summary>
    public async Task<ManagedDiscoveryReviewResponse> ReviewAsync(
        Guid connectionId,
        ManagedDiscoveryReviewRequest input,
        CancellationToken token) {
        var (connection, review, metadata) = await ResolveReviewAsync(connectionId, input.EntityKind, input.ExternalIdentity, token);
        return new(connection.Connection.State.Revision, review, metadata);
    }

    /// <summary>Creates or enriches a wanted movie only after refreshing the exact connection-scoped review.</summary>
    public async Task<PreparedWantedMovieResponse> PrepareAsync(
        Guid connectionId,
        PrepareManagedDiscoveryRequest input,
        CancellationToken token) {
        if (input?.Request is null) throw new RequestCommitValidationException("Submit the reviewed manager movie.");
        var request = input.Request;
        var (connection, review, _) = await ResolveReviewAsync(connectionId, EntityKind.Movie, request.RootExternalIdentity, token);
        if (connection.Connection.State.Revision != input.ConnectionRevision)
            throw new ConnectionConflictException("The selected manager connection changed. Review the movie again.");
        if (!string.Equals(request.PluginId, connection.Manifest.Id, StringComparison.OrdinalIgnoreCase))
            throw new RequestCommitValidationException("The reviewed movie belongs to another manager connection.");
        if (!string.Equals(request.ProposalRevision, review.Revision, StringComparison.Ordinal))
            throw new RequestProposalChangedException();
        return await wanted.PrepareFromManagerAsync(request, review, token);
    }

    private async Task<(AuthorizedIntegrationConnection Connection, RequestReviewResponse Review, ManagedDiscoveryMetadata? Metadata)> ResolveReviewAsync(
        Guid connectionId,
        EntityKind kind,
        ExternalIdentity identity,
        CancellationToken token) {
        if (kind != EntityKind.Movie || !CanonicalMovieIdentity(identity))
            throw new ArgumentException("Select a manager-discovered movie with its canonical TMDB identity.");
        var discoveryConnection = await access.RequireAsync(
            connectionId,
            PluginCapability.ExternalManager,
            IntegrationOperation.DiscoverManaged,
            kind,
            token);
        var connection = await access.RequireAsync(
            connectionId,
            PluginCapability.ExternalManager,
            IntegrationOperation.LookupManaged,
            kind,
            token);
        if (discoveryConnection.Connection.State.Revision != connection.Connection.State.Revision)
            throw new ConnectionConflictException("The selected manager connection changed. Review the movie again.");
        var work = new ManagedLookupInput(kind, new Dictionary<string, string> { [identity.Namespace] = identity.Value });
        var result = await lookup.LookupAsync(connection.Manifest.Id, connection.Context, work, token);
        ManagedCreationEvidence.ValidateLookup(work, result);
        ValidateCandidate(new(result.Candidate.EntityKind, result.Candidate.Title, result.Candidate.Year,
            result.Candidate.ExternalIds, result.Candidate.Metadata), kind);
        var proposal = Proposal(connectionId, connection.Manifest.Id, result.Candidate);
        var review = new RequestReviewResponse(
            connection.Manifest.Id,
            identity,
            kind,
            RequestMediaKind.Movie,
            proposal,
            RequestProposalRevision.Compute(proposal),
            [new(proposal.ProposalId, RequestMediaKind.Movie, kind, identity, true, Year: result.Candidate.Year)]);
        return (connection, review, result.Candidate.Metadata);
    }

    private static EntityMetadataProposal Proposal(Guid connectionId, string pluginId, ManagedCandidate candidate) {
        var metadata = candidate.Metadata;
        var dates = (metadata?.Dates ?? new Dictionary<string, string>())
            .Select(pair => pair.Key.TryDecodeAs<EntityDateType>(out var type) ? new EntityMetadataDatePatch(type, pair.Value) : null)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
        var images = new List<ImageCandidate>();
        if (metadata?.PosterUrl is { } poster)
            images.Add(new(MediaImageKind.Poster.ToCode(), poster, pluginId, null, null, null, null));
        if (metadata?.BackdropUrl is { } backdrop)
            images.Add(new(MediaImageKind.Backdrop.ToCode(), backdrop, pluginId, null, null, null, null));
        var patch = new EntityMetadataPatch(
            candidate.Title,
            metadata?.Overview,
            candidate.ExternalIds,
            metadata?.Urls ?? [],
            metadata?.Tags ?? [],
            metadata?.Studio,
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            metadata?.Classification) {
            AlternativeTitles = string.IsNullOrWhiteSpace(metadata?.OriginalTitle)
                || string.Equals(metadata.OriginalTitle, candidate.Title, StringComparison.Ordinal)
                    ? []
                    : [metadata.OriginalTitle],
            DateEntries = dates
        };
        var identity = CanonicalIdentity(candidate.EntityKind, candidate.ExternalIds);
        return new($"manager:{connectionId:D}:{identity.Namespace}:{identity.Value}", pluginId, candidate.EntityKind, null,
            DiscoveryMatchReason, patch, images, [], [], Relationships: []);
    }

    private static void ValidateSearch(ManagedDiscoveryQuery input) {
        if (input is null || input.EntityKind != EntityKind.Movie || string.IsNullOrWhiteSpace(input.Query)
            || input.Query.Length > 512 || input.Query.Any(char.IsControl) || input.Limit is < 1 or > 100)
            throw new ArgumentException("Enter a movie title up to 512 characters and a result limit from 1 to 100.");
    }

    private static void ValidateCandidate(ManagedDiscoveryCandidate candidate, EntityKind kind) {
        if (candidate is null || candidate.EntityKind != kind || !Text(candidate.Title, 512)
            || candidate.Year is < 0 or > 9999 || !Identities(candidate.ExternalIds) || !ValidMetadata(candidate.Metadata))
            throw InvalidEvidence();
        _ = CanonicalIdentity(kind, candidate.ExternalIds);
    }

    private static bool ValidMetadata(ManagedDiscoveryMetadata? value) => value is null
        || (OptionalText(value.OriginalTitle, 512) && OptionalBlock(value.Overview, 32_768)
            && OptionalText(value.Studio, 512) && OptionalText(value.Classification, 128)
            && value.RuntimeMinutes is null or >= 1 and <= 10_080 && value.Rating is null or >= 0 and <= 10
            && OptionalList(value.Tags, 64, 128) && OptionalDictionary(value.Dates, 32, 128, 128)
            && OptionalList(value.Urls, 64, 8192) && (value.Urls is null || value.Urls.All(SafeUrl))
            && (value.PosterUrl is null || SafeImageUrl(value.PosterUrl))
            && (value.BackdropUrl is null || SafeImageUrl(value.BackdropUrl)));

    private static ExternalIdentity CanonicalIdentity(EntityKind kind, IReadOnlyDictionary<string, string> ids) {
        if (kind == EntityKind.Movie && ids.TryGetValue(ExternalIdProviders.Tmdb, out var tmdb)
            && CanonicalMovieIdentity(new(ExternalIdProviders.Tmdb, tmdb))) return new(ExternalIdProviders.Tmdb, tmdb);
        throw InvalidEvidence();
    }

    private static bool CanonicalMovieIdentity(ExternalIdentity? identity) => identity?.Namespace == ExternalIdProviders.Tmdb
        && int.TryParse(identity.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
        && id.ToString(CultureInfo.InvariantCulture) == identity.Value;
    private static bool Identities(IReadOnlyDictionary<string, string>? values) => values is { Count: > 0 and <= 64 }
        && values.All(pair => Text(pair.Key, 128) && Text(pair.Value, 2048));
    private static bool OptionalList(IReadOnlyList<string>? values, int count, int length) => values is null
        || values.Count <= count && values.All(value => Text(value, length)) && values.Distinct(StringComparer.Ordinal).Count() == values.Count;
    private static bool OptionalDictionary(IReadOnlyDictionary<string, string>? values, int count, int keyLength, int valueLength) => values is null
        || values.Count <= count && values.All(pair => Text(pair.Key, keyLength) && Text(pair.Value, valueLength));
    private static bool OptionalText(string? value, int limit) => value is null || Text(value, limit);
    private static bool OptionalBlock(string? value, int limit) => value is null || !string.IsNullOrWhiteSpace(value) && value.Length <= limit
        && !value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));
    private static bool Text(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl);
    private static bool SafeImageUrl(string value) => SafeUrl(value) && !new Uri(value).IsLoopback;
    private static bool SafeUrl(string value) => value.Length <= 8192 && !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character == '\\')
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
    private static IntegrationInvocationException InvalidEvidence() => new("The manager returned invalid, oversized, or mismatched discovery evidence.");
}
