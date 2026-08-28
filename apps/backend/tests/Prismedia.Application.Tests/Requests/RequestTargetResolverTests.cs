using Prismedia.Application.Acquisition;
using Prismedia.Application.Requests;
using Prismedia.Application.Security;
using Prismedia.Application.Settings;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.Settings;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Requests;

public sealed class RequestTargetResolverTests {
    private static readonly Guid AllowedRootId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid HiddenRootId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ProfileId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task MemberDefaultsStayInsideAccessibleCompatibleLibraries() {
        var resolver = CreateResolver();
        var descriptor = RequestKindRegistry.Find(RequestMediaKind.Movie)!;

        var resolved = await resolver.ResolveAsync(
            descriptor,
            new AcquisitionTargeting(null, null),
            hideNsfw: true,
            CancellationToken.None);

        Assert.Equal(AllowedRootId, resolved.TargetLibraryRootId);
        Assert.Equal(ProfileId, resolved.ProfileId);
    }

    [Fact]
    public async Task MemberCannotTargetAnInaccessibleLibrary() {
        var resolver = CreateResolver();
        var descriptor = RequestKindRegistry.Find(RequestMediaKind.Movie)!;

        var exception = await Assert.ThrowsAsync<RequestCommitValidationException>(() =>
            resolver.ResolveAsync(
                descriptor,
                new AcquisitionTargeting(HiddenRootId, null),
                hideNsfw: true,
                CancellationToken.None));

        Assert.Contains("not available", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static RequestTargetResolver CreateResolver() {
        var roots = new[] {
            Root(AllowedRootId, "Movies"),
            Root(HiddenRootId, "Hidden")
        };
        var profile = new BookAcquisitionProfileView(
            ProfileId,
            EntityKind.Movie,
            "Household movies",
            IsDefault: true,
            AllowedRootId,
            "{Title}",
            ImportMode.Move,
            [],
            [],
            0,
            null,
            null,
            [],
            [],
            [],
            [],
            AutoPick: true,
            AutoRedownload: false,
            UpgradeUntilCutoff: false,
            BookSourceTier.Unknown,
            BookFormatTier.Unknown);
        var profileStore = new StubProfileStore([profile]);
        IReadOnlySet<Guid> allowedRoots = new HashSet<Guid> { AllowedRootId };
        return new RequestTargetResolver(
            new MemberContext(allowedRoots),
            new SettingsService(new StubSettingsPersistence(roots)),
            new BookAcquisitionProfileCommandService(profileStore, new MemberContext(allowedRoots)));
    }

    private static LibraryRoot Root(Guid id, string label) {
        var now = DateTimeOffset.UtcNow;
        return new LibraryRoot(
            id, $"/media/{label}", label, Enabled: true, Recursive: true,
            ScanVideos: true, ScanImages: false, ScanAudio: false, ScanBooks: false,
            IsNsfw: false, LastScannedAt: null, now, now);
    }

    private sealed class MemberContext(IReadOnlySet<Guid> allowedRootIds) : ICurrentUserContext {
        public bool IsAuthenticated => true;
        public bool IsSystem => false;
        public Guid UserId { get; } = Guid.Parse("44444444-4444-4444-4444-444444444444");
        public Guid SessionId { get; } = Guid.Parse("55555555-5555-5555-5555-555555555555");
        public string Username => "member";
        public UserRole Role => UserRole.Member;
        public bool IsAdmin => false;
        public bool AllowNsfw => false;
        public bool CanCreateLibraries => false;
        public bool CanRequestContent => true;
        public ValueTask<IReadOnlySet<Guid>?> GetAllowedLibraryRootIdsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlySet<Guid>?>(allowedRootIds);
    }

    private sealed class StubSettingsPersistence(IReadOnlyList<LibraryRoot> roots) : ISettingsPersistence {
        public Task<IReadOnlyList<LibraryRoot>> ListLibraryRootsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(roots);

        public Task<IReadOnlyDictionary<string, string>> LoadSettingOverridesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveSettingOverrideAsync(string key, string valueJson, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveSettingOverridesAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplaceSettingOverridesAsync(IReadOnlyDictionary<string, string> upserts, IReadOnlyCollection<string> deletes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteSettingOverrideAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LibraryRoot?> GetLibraryRootAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LibraryRoot> AddLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LibraryRoot> SaveLibraryRootAsync(LibraryRoot state, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteLibraryRootAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubProfileStore(IReadOnlyList<BookAcquisitionProfileView> profiles) : IBookAcquisitionProfileStore {
        public Task<IReadOnlyList<BookAcquisitionProfileView>> ListAsync(bool hideNsfw, IReadOnlySet<Guid>? allowedRootIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BookAcquisitionProfileView>>(
                profiles.Where(profile => allowedRootIds is null || allowedRootIds.Contains(profile.TargetLibraryRootId)).ToArray());

        public Task<BookAcquisitionRules> GetRulesAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BookImportProfile?> GetImportProfileAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GetAutoPickAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GetAutoRedownloadAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetDownloadCategoryAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BookAcquisitionProfileView?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BookAcquisitionProfileView> SaveAsync(BookAcquisitionProfileSaveCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
