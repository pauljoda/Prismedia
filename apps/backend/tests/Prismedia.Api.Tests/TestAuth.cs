using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Entities;
using Prismedia.Application.Security;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

internal static class TestAuth {
    internal const string ApiKey = "bava-cada-dafa";

    internal static WebApplicationFactory<Program> WithTestAuth(
        this WebApplicationFactory<Program> factory,
        bool allowNsfw = false,
        bool allowSfw = true) =>
        factory.WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                services.RemoveAll<ISecurityPersistence>();
                services.AddSingleton<ISecurityPersistence>(new FakeSecurityPersistence(allowSfw, allowNsfw));
            });
        });

    internal static HttpClient CreateAuthenticatedClient(this WebApplicationFactory<Program> factory) {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Prismedia-Api-Key", ApiKey);
        return client;
    }

    internal sealed class VisibleEntityReadService : IEntityReadService {
        public Task<EntityListResponse> ListAsync(
            string? kind,
            string? query,
            string? cursor,
            bool? hideNsfw,
            int? limit,
            CancellationToken cancellationToken,
            Guid? referencedBy = null,
            string? relationshipCode = null,
            string? sort = null,
            string? sortDir = null,
            int? seed = null,
            bool? favorite = null,
            bool? organized = null,
            int? ratingMin = null,
            int? ratingMax = null,
            bool? unrated = null,
            string? status = null,
            string? bookType = null,
            string? bookFormat = null,
            bool? nsfw = null,
            bool? hasFile = null,
            bool? played = null,
            bool? orphaned = null,
        bool? wanted = null) =>
            Task.FromResult(new EntityListResponse([], null, 0));

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<EntityCard?>(Card(id));

        public Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            Task.FromResult(new EntityThumbnailBatchResponse([]));

        public Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IEntityCard?>(Card(id) with { Kind = kind.DecodeAs<EntityKind>() });

        private static EntityCard Card(Guid id) =>
            new() {
                Id = id,
                Kind = EntityKind.Video,
                Title = "Visible Video",
                ParentEntityId = null,
                SortOrder = null,
                Capabilities = [],
                ChildrenByKind = [],
                Relationships = []
            };
    }

    private sealed class FakeSecurityPersistence : ISecurityPersistence {
        private static readonly Guid ServerId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        private static readonly Guid ProfileId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        private readonly Dictionary<string, JellyfinSession> _sessions = new(StringComparer.Ordinal);
        private string _apiKey = ApiKey;
        private JellyfinProfile _profile;

        public FakeSecurityPersistence(bool allowSfw, bool allowNsfw) =>
            _profile = Profile(DateTimeOffset.UtcNow, allowSfw, allowNsfw);

        public Task<AppSecurityState> EnsureSecurityAsync(Func<string> keyFactory, CancellationToken cancellationToken) =>
            Task.FromResult(State());

        public Task<AppSecurityState> GetSecurityAsync(Func<string> keyFactory, CancellationToken cancellationToken) =>
            Task.FromResult(State());

        public Task<ApiKeyRotationResult> RotateApiKeyAsync(string apiKey, CancellationToken cancellationToken) {
            var invalidated = _sessions.Count;
            _sessions.Clear();
            _apiKey = apiKey;
            return Task.FromResult(new ApiKeyRotationResult(State(), invalidated));
        }

        public Task<IReadOnlyList<JellyfinProfile>> ListProfilesAsync(bool includeDisabled, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JellyfinProfile>>([_profile]);

        public Task<JellyfinProfile?> GetProfileAsync(Guid profileId, CancellationToken cancellationToken) =>
            Task.FromResult<JellyfinProfile?>(profileId == ProfileId ? _profile : null);

        public Task<JellyfinProfile?> FindProfileByUsernameAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult<JellyfinProfile?>(username.Equals(_profile.Username, StringComparison.OrdinalIgnoreCase) ? _profile : null);

        public Task<JellyfinProfile> CreateProfileAsync(
            string username,
            string displayName,
            bool allowSfw,
            bool allowNsfw,
            bool enabled,
            CancellationToken cancellationToken) {
            var now = DateTimeOffset.UtcNow;
            _profile = new JellyfinProfile(Guid.NewGuid(), username, displayName, allowSfw, allowNsfw, enabled, null, now, now);
            return Task.FromResult(_profile);
        }

        public Task<JellyfinProfile?> UpdateProfileAsync(
            Guid profileId,
            string? username,
            string? displayName,
            bool? allowSfw,
            bool? allowNsfw,
            bool? enabled,
            CancellationToken cancellationToken) {
            if (profileId != _profile.Id) {
                return Task.FromResult<JellyfinProfile?>(null);
            }

            _profile = _profile with {
                Username = username ?? _profile.Username,
                DisplayName = displayName ?? _profile.DisplayName,
                AllowSfw = allowSfw ?? _profile.AllowSfw,
                AllowNsfw = allowNsfw ?? _profile.AllowNsfw,
                Enabled = enabled ?? _profile.Enabled,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return Task.FromResult<JellyfinProfile?>(_profile);
        }

        public Task<bool> DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken) =>
            Task.FromResult(profileId == _profile.Id);

        public Task<JellyfinSession> CreateSessionAsync(
            Guid profileId,
            string tokenHash,
            JellyfinClientIdentity client,
            CancellationToken cancellationToken) {
            var now = DateTimeOffset.UtcNow;
            var session = new JellyfinSession(
                Guid.NewGuid(),
                profileId,
                tokenHash,
                client.Client,
                client.DeviceName,
                client.DeviceId,
                client.ApplicationVersion,
                now,
                now,
                null);
            _sessions[tokenHash] = session;
            return Task.FromResult(session);
        }

        public Task<JellyfinSessionResolution?> ResolveSessionAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(_sessions.TryGetValue(tokenHash, out var session)
                ? new JellyfinSessionResolution(session, _profile)
                : null);

        public Task TouchProfileLoginAsync(Guid profileId, CancellationToken cancellationToken) {
            if (profileId == _profile.Id) {
                _profile = _profile with { LastLoginAt = DateTimeOffset.UtcNow };
            }

            return Task.CompletedTask;
        }

        private AppSecurityState State() {
            var now = DateTimeOffset.UtcNow;
            return new AppSecurityState(1, ServerId, _apiKey, true, now, now, now, now);
        }

        private static JellyfinProfile Profile(DateTimeOffset now, bool allowSfw, bool allowNsfw) =>
            new(ProfileId, "Prismedia", "Prismedia", allowSfw, allowNsfw, true, null, now, now);
    }
}
