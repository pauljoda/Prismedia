using System.Globalization;
using Prismedia.Application.Plugins;
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
    ReviewedWantedMovieService wanted,
    IPluginIdentityRouter identityRouter,
    IPluginRequestReviewSource metadataReviews,
    IIdentifyProviderService identifyProviders) {
    private const int MaximumCredits = 1000;
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
        var (connection, review, metadata) = await ResolveReviewAsync(connectionId, input.EntityKind, input.ExternalIdentity, null, token);
        return new(connection.Connection.State.Revision, review, metadata);
    }

    /// <summary>Refreshes and fences one manager-origin review while preserving the user's filtered proposal.</summary>
    internal async Task<(long Revision, ReviewedRequestCommitRequest Request)> CanonicalizeAsync(
        Guid connectionId,
        long expectedRevision,
        ReviewedRequestCommitRequest request,
        CancellationToken token) {
        if (request is null) throw new RequestCommitValidationException("Submit the reviewed manager movie.");
        var kind = request.Kind switch {
            RequestMediaKind.Movie => EntityKind.Movie,
            RequestMediaKind.Series => EntityKind.VideoSeries,
            _ => throw new RequestCommitValidationException("Submit a reviewed manager movie or series.")
        };
        var (connection, review, _) = await ResolveReviewAsync(
            connectionId,
            kind,
            kind == EntityKind.VideoSeries ? ManagerSeriesIdentity(request) : request.RootExternalIdentity,
            request.PluginId,
            token);
        if (connection.Connection.State.Revision != expectedRevision)
            throw new ConnectionConflictException("The selected manager connection changed. Review the title again.");
        if (kind == EntityKind.Movie
            && !string.Equals(request.PluginId, connection.Manifest.Id, StringComparison.OrdinalIgnoreCase))
            throw new RequestCommitValidationException("The reviewed title belongs to another manager connection.");
        if (kind == EntityKind.VideoSeries && request.RootExternalIdentity != review.ExternalIdentity)
            throw new RequestCommitValidationException(
                "The reviewed metadata series no longer matches the exact Sonarr TVDB identity.");
        if (!string.Equals(request.ProposalRevision, review.Revision, StringComparison.Ordinal))
            throw new RequestProposalChangedException();
        return (connection.Connection.State.Revision, request with { Review = review });
    }

    private static ExternalIdentity ManagerSeriesIdentity(ReviewedRequestCommitRequest request) {
        var primary = ManagedFulfillmentPolicy.For(EntityKind.VideoSeries).IdentityFormats[0];
        return primary.Find(request.Proposal?.Patch.ExternalIds ?? new Dictionary<string, string>())
            ?? throw new RequestCommitValidationException(
                "The reviewed series no longer includes the manager's exact series identity.");
    }

    /// <summary>Creates or enriches a wanted movie only after refreshing the exact connection-scoped review.</summary>
    public async Task<PreparedWantedMovieResponse> PrepareAsync(
        Guid connectionId,
        PrepareManagedDiscoveryRequest input,
        CancellationToken token) {
        if (input?.Request is null) throw new RequestCommitValidationException("Submit the reviewed manager movie.");
        var request = input.Request;
        var (connection, review, _) = await ResolveReviewAsync(connectionId, EntityKind.Movie, request.RootExternalIdentity, null, token);
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
        string? expectedReviewPluginId,
        CancellationToken token) {
        if (!SupportedDiscoveryIdentity(kind, identity))
            throw new ArgumentException("Select a manager-discovered movie or series with its canonical identity.");
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
            throw new ConnectionConflictException("The selected manager connection changed. Review the title again.");
        var work = new ManagedLookupInput(kind, new Dictionary<string, string> { [identity.Namespace] = identity.Value });
        var result = await lookup.LookupAsync(connection.Manifest.Id, connection.Context, work, token);
        ManagedCreationEvidence.ValidateLookup(work, result);
        ValidateCandidate(new(result.Candidate.EntityKind, result.Candidate.Title, result.Candidate.Year,
            result.Candidate.ExternalIds, result.Candidate.Metadata), kind);
        if (kind == EntityKind.VideoSeries) {
            var seriesReview = await ResolveSeriesReviewAsync(result.Candidate, expectedReviewPluginId, token);
            return (connection, seriesReview, result.Candidate.Metadata);
        }
        var proposal = ManagedMetadataProposalFactory.Create(
            connectionId,
            connection.Manifest.Id,
            result.Candidate);
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

    private async Task<RequestReviewResponse> ResolveSeriesReviewAsync(
        ManagedCandidate candidate,
        string? expectedPluginId,
        CancellationToken token) {
        var identities = candidate.ExternalIds
            .Where(pair => IsPinningIdentity(EntityKind.VideoSeries, new(pair.Key, pair.Value)))
            .Select(pair => new ExternalIdentity(pair.Key, pair.Value))
            .ToArray();
        var routes = await identityRouter.ResolveAsync(
                EntityKind.VideoSeries.ToCode(),
                IdentifyAction.LookupId,
                identities,
                token);
        var providerOrder = (await identifyProviders.ListProvidersAsync(EntityKind.VideoSeries.ToCode(), token))
            .Where(provider => provider.Enabled && provider.MissingAuthKeys.Count == 0)
            .Select((provider, index) => (provider.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.OrdinalIgnoreCase);
        var orderedRoutes = routes
            .Where(route => expectedPluginId is null
                || string.Equals(route.PluginId, expectedPluginId, StringComparison.OrdinalIgnoreCase))
            .Where(route => providerOrder.ContainsKey(route.PluginId))
            .OrderBy(route => providerOrder[route.PluginId])
            .ThenBy(route => route.Identity.Namespace == ExternalIdProviders.Tmdb ? 0 : 1)
            .ToArray();
        foreach (var route in orderedRoutes) {
            var review = await metadataReviews.ReviewAsync(
                new(RequestMediaKind.Series, route.PluginId, route.Identity),
                hideNsfw: false,
                token);
            if (ValidSeriesReview(review, route, candidate)) return MergeManagerSeriesIdentity(review!, candidate);
        }
        throw new RequestCommitValidationException(
            "No enabled metadata provider can review this manager series with its confirmed TVDB or TMDB identity.");
    }

    private static bool ValidSeriesReview(
        RequestReviewResponse? review,
        PluginIdentityRoute route,
        ManagedCandidate candidate) {
        if (review is null || review.Kind != RequestMediaKind.Series || review.EntityKind != EntityKind.VideoSeries
            || review.Proposal.TargetKind != EntityKind.VideoSeries || review.ExternalIdentity != route.Identity
            || !string.Equals(review.PluginId, route.PluginId, StringComparison.OrdinalIgnoreCase)) return false;
        return candidate.ExternalIds.GetValueOrDefault(route.Identity.Namespace) == route.Identity.Value
            && review.Proposal.Patch.ExternalIds.GetValueOrDefault(route.Identity.Namespace) == route.Identity.Value;
    }

    private static RequestReviewResponse MergeManagerSeriesIdentity(
        RequestReviewResponse review,
        ManagedCandidate candidate) {
        var externalIds = review.Proposal.Patch.ExternalIds.ToDictionary(StringComparer.Ordinal);
        foreach (var pair in candidate.ExternalIds.Where(pair =>
                     pair.Key is ExternalIdProviders.Tvdb or ExternalIdProviders.Tmdb or ExternalIdProviders.Imdb)) {
            if (externalIds.TryGetValue(pair.Key, out var value) && value != pair.Value) throw InvalidEvidence();
            externalIds[pair.Key] = pair.Value;
        }
        var proposal = review.Proposal with {
            Patch = review.Proposal.Patch with { ExternalIds = externalIds }
        };
        return review with { Proposal = proposal, Revision = RequestProposalRevision.Compute(proposal) };
    }

    private static void ValidateSearch(ManagedDiscoveryQuery input) {
        if (input is null || input.EntityKind is not (EntityKind.Movie or EntityKind.VideoSeries or EntityKind.ComicSeries) || string.IsNullOrWhiteSpace(input.Query)
            || input.Query.Length > 512 || input.Query.Any(char.IsControl) || input.Limit is < 1 or > 100)
            throw new ArgumentException("Enter a title up to 512 characters and a result limit from 1 to 100.");
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
            && (value.BackdropUrl is null || SafeImageUrl(value.BackdropUrl))
            && ValidCredits(value.Credits));

    private static bool ValidCredits(IReadOnlyList<ManagedPersonCredit>? credits) => credits is null
        || credits.Count <= MaximumCredits && credits.All(credit => credit is not null
            && Text(credit.Name, 512)
            && Enum.IsDefined(credit.Role)
            && OptionalText(credit.Character, 512)
            && credit.SortOrder is null or >= 0 and <= 1_000_000
            && PersonIdentities(credit.ExternalIds)
            && (credit.ProfileUrl is null || SafeImageUrl(credit.ProfileUrl)));

    private static bool PersonIdentities(IReadOnlyDictionary<string, string>? values) => values is null
        || values.Count <= 8 && values.All(pair => Text(pair.Key, 128) && Text(pair.Value, 2048))
        && (!values.ContainsKey(ExternalIdProviders.Tmdb) || TryCanonicalTmdbPersonId(values, out _));

    private static bool TryCanonicalTmdbPersonId(
        IReadOnlyDictionary<string, string> values,
        out string tmdb) {
        if (values.TryGetValue(ExternalIdProviders.Tmdb, out var value)
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            && id > 0
            && id.ToString(CultureInfo.InvariantCulture) == value) {
            tmdb = value;
            return true;
        }
        tmdb = string.Empty;
        return false;
    }

    private static ExternalIdentity CanonicalIdentity(EntityKind kind, IReadOnlyDictionary<string, string> ids) =>
        ManagedFulfillmentPolicy.For(kind).PinningIdentity(ids) ?? throw InvalidEvidence();

    /// <summary>
    /// Manager discovery reviews movies from the manager's own metadata and series from a metadata plugin matched
    /// by the manager's pinning identity; both require one of the kind's canonical pinning identities.
    /// </summary>
    private static bool SupportedDiscoveryIdentity(EntityKind kind, ExternalIdentity? identity) =>
        kind is EntityKind.Movie or EntityKind.VideoSeries && IsPinningIdentity(kind, identity);

    private static bool IsPinningIdentity(EntityKind kind, ExternalIdentity? identity) =>
        identity is not null && ManagedFulfillmentPolicy.For(kind).IdentityFormats
            .Any(format => format.Provider == identity.Namespace && format.IsCanonical(identity.Value));

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
