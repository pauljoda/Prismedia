using Prismedia.Contracts.Integrations;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Validates connected-library observations without claiming local availability or taking acquisition ownership.</summary>
public sealed class ManagedLibraryService(IntegrationConnectionAccess access, IIntegrationManagerGateway gateway) {
    /// <summary>Reads a bounded page of existing holdings from the currently authorized instance.</summary>
    public async Task<ManagedLibraryPage> SearchAsync(Guid connectionId, ManagedLibraryQuery input, CancellationToken cancellationToken) {
        if (!Enum.IsDefined(input.EntityKind) || input.Limit is < 1 or > 100 || input.Query?.Length > 512 || input.Cursor?.Length > 8192)
            throw new ArgumentException("Choose a valid kind, query up to 512 characters, and page size from 1 to 100.");
        var authorized = await access.RequireAsync(connectionId, PluginCapability.ConnectedLibrary, IntegrationOperation.SearchLibrary, input.EntityKind, cancellationToken);
        var page = await gateway.SearchLibraryAsync(authorized.Manifest.Id, authorized.Context, input, cancellationToken);
        if (page.Items is null || page.Items.Count > input.Limit || page.NextCursor?.Length > 8192
            || page.Items.Select(item => item?.RemoteId).Distinct().Count() != page.Items.Count) throw Invalid();
        foreach (var item in page.Items) ValidateItem(item, input.EntityKind);
        return page;
    }

    /// <summary>Returns remote file evidence only after verifying the selected holding's identity.</summary>
    public async Task<ManagedItemSnapshot> GetAsync(Guid connectionId, ManagedItemInput input, CancellationToken cancellationToken) {
        if (!IsValidInput(input))
            throw new ArgumentException("Select an existing holding with its current identities.");
        var authorized = await access.RequireAsync(connectionId, PluginCapability.ConnectedLibrary, IntegrationOperation.GetLibraryItem, input.EntityKind, cancellationToken);
        var snapshot = await gateway.GetLibraryItemAsync(authorized.Manifest.Id, authorized.Context, input, cancellationToken);
        ValidateSnapshot(input, snapshot);
        return snapshot;
    }

    /// <summary>Checks whether a saved holding reference contains the complete bounded identity pin required for a safe lookup.</summary>
    public static bool IsValidInput(ManagedItemInput? input) => input is not null
        && Enum.IsDefined(input.EntityKind)
        && Text(input.RemoteId, 512)
        && Identities(input.ExpectedExternalIds);

    /// <summary>Validates exact holding identity and complete finite file evidence before any application use case trusts it.</summary>
    public static void ValidateSnapshot(ManagedItemInput input, ManagedItemSnapshot snapshot) {
        if (snapshot is null) throw Invalid();
        ValidateItem(snapshot.Item, input.EntityKind);
        if (snapshot.Item.RemoteId != input.RemoteId || input.ExpectedExternalIds.Any(pair => snapshot.Item.ExternalIds.GetValueOrDefault(pair.Key) != pair.Value)
            || !Text(snapshot.Path, 8192) || snapshot.ObservedAt == default || snapshot.ObservedAt > DateTimeOffset.UtcNow.AddMinutes(5)
            || snapshot.Files is null || snapshot.Files.Count > 10000 || snapshot.Files.Select(file => file?.RemoteId).Distinct().Count() != snapshot.Files.Count)
            throw Invalid();
        foreach (var file in snapshot.Files) {
            if (file is null || !Text(file.RemoteId, 512) || !Text(file.Path, 8192) || file.SizeBytes <= 0
                || file.Targets is not { Count: > 0 and <= 1000 } || file.Targets.Select(target => target?.RemoteId).Distinct().Count() != file.Targets.Count) throw Invalid();
            foreach (var target in file.Targets) {
                if (target is null || !Text(target.RemoteId, 512) || !Text(target.Title, 512) || !Enum.IsDefined(target.EntityKind)
                    || target.SeasonNumber < 0 || target.EpisodeNumber < 0 || target.AbsoluteNumber < 0
                    || target.IssueLabel is not null && (!Text(target.IssueLabel, 128) || target.EntityKind != EntityKind.ComicInstallment)) throw Invalid();
            }
        }
        var targets = snapshot.Files.SelectMany(file => file.Targets).ToArray();
        if (targets.Select(target => target.RemoteId).Distinct(StringComparer.Ordinal).Count() != targets.Length
            || input.EntityKind == EntityKind.Movie && targets.Any(target => target.EntityKind != EntityKind.Movie || target.RemoteId != input.RemoteId
                || target.SeasonNumber is not null || target.EpisodeNumber is not null || target.AbsoluteNumber is not null)
            || input.EntityKind == EntityKind.VideoSeries && targets.Any(target => target.EntityKind != EntityKind.VideoEpisode
                || target.SeasonNumber is null || target.EpisodeNumber is null)
            || input.EntityKind == EntityKind.ComicSeries && targets.Any(target => target.EntityKind != EntityKind.ComicInstallment
                || !Text(target.IssueLabel, 128) || target.SeasonNumber is not null || target.EpisodeNumber is not null || target.AbsoluteNumber is not null)) throw Invalid();
        if (snapshot.ComicIssues is { } issues) {
            if (input.EntityKind != EntityKind.ComicSeries || issues.Count > 10000
                || issues.Any(issue => issue is null || !Text(issue.RemoteId, 512) || !Text(issue.IssueLabel, 128) || !Text(issue.Title, 512)
                    || issue.ExternalIds is not null && !Identities(issue.ExternalIds))
                || issues.Select(issue => issue.RemoteId).Distinct(StringComparer.Ordinal).Count() != issues.Count)
                throw Invalid();
            var comicVineIds = issues.Select(issue => issue.ExternalIds?.GetValueOrDefault(ExternalIdProviders.ComicVine))
                .OfType<string>().ToArray();
            if (comicVineIds.Distinct(StringComparer.Ordinal).Count() != comicVineIds.Length) throw Invalid();
            var issueById = issues.ToDictionary(issue => issue.RemoteId, StringComparer.Ordinal);
            if (targets.Any(target => !issueById.TryGetValue(target.RemoteId, out var issue)
                || target.IssueLabel != issue.IssueLabel || target.Title != issue.Title)) throw Invalid();
        }
    }

    /// <summary>Reads existing profiles and folders without persisting defaults or issuing remote commands.</summary>
    public async Task<ManagerOptions> OptionsAsync(Guid connectionId, ManagerOptionsInput input, CancellationToken cancellationToken) {
        var authorized = await access.RequireAsync(connectionId, PluginCapability.ExternalManager, IntegrationOperation.ManagerOptions, input.EntityKind, cancellationToken);
        var options = await gateway.GetOptionsAsync(authorized.Manifest.Id, authorized.Context, input, cancellationToken);
        if (options.Profiles is null || options.Roots is null || options.Profiles.Count > 1000 || options.Roots.Count > 1000
            || options.Profiles.Any(choice => choice is null || !Text(choice.Id, 512) || !Text(choice.Label, 512))
            || options.Roots.Any(root => root is null || !Text(root.Id, 512) || !Text(root.Path, 8192))
            || options.Profiles.Select(choice => choice.Id).Distinct().Count() != options.Profiles.Count
            || options.Roots.Select(root => root.Id).Distinct().Count() != options.Roots.Count) throw Invalid();
        return options;
    }

    private static void ValidateItem(ManagedLibraryItem item, EntityKind kind) {
        if (item is null || item.EntityKind != kind || !Text(item.RemoteId, 512) || !Text(item.Title, 512)
            || !Identities(item.ExternalIds) || item.Year is < 0 or > 9999 || item.RemoteFileCount < 0
            || item.ProfileId is not null && !Text(item.ProfileId, 512)
            || !ValidPresentation(item.Presentation)) throw Invalid();
    }
    private static bool ValidPresentation(ManagedLibraryPresentation? presentation) => presentation is null ||
        ((presentation.Overview is null || TextBlock(presentation.Overview, 32_768))
            && (presentation.PosterUrl is null || SafeImageUrl(presentation.PosterUrl))
            && (presentation.BackdropUrl is null || SafeImageUrl(presentation.BackdropUrl))
            && presentation.Genres is null or { Count: <= 64 }
            && (presentation.Genres is null || presentation.Genres.All(value => Text(value, 128))
                && presentation.Genres.Distinct(StringComparer.Ordinal).Count() == presentation.Genres.Count)
            && presentation.RuntimeMinutes is null or >= 1 and <= 10_080
            && (presentation.ContentRating is null || Text(presentation.ContentRating, 128)));
    private static bool SafeImageUrl(string value) => value.Length <= 8192
        && !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character == '\\')
        && Uri.TryCreate(value, UriKind.Absolute, out var address)
        && address.Scheme is "http" or "https" && address.Host.Length > 0
        && address.UserInfo.Length == 0 && address.Fragment.Length == 0;
    private static bool TextBlock(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit
        && !value.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'));
    private static bool Identities(IReadOnlyDictionary<string, string>? ids) => ids is { Count: > 0 and <= 64 }
        && ids.All(pair => Text(pair.Key, 128) && Text(pair.Value, 2048));
    private static bool Text(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl);
    private static IntegrationInvocationException Invalid() => new("The connected library returned invalid, oversized, or mismatched holding evidence.");
}
