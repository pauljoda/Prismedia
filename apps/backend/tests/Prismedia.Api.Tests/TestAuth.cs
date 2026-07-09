using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Entities;
using Prismedia.Application.Security;
using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Security;

namespace Prismedia.Api.Tests;

internal static class TestAuth {
    /// <summary>Seeded admin account used by authenticated test clients.</summary>
    internal const string Username = "Prismedia";

    /// <summary>Password of the seeded admin account.</summary>
    internal const string Password = "prismedia-test-password";

    /// <summary>Pre-issued session token sent by <see cref="CreateAuthenticatedClient"/>.</summary>
    internal const string Token = "prismedia-test-session-token";

    private static readonly Lazy<string> PasswordHash =
        new(() => new IdentityPasswordHasher().Hash(Password));

    internal static WebApplicationFactory<Program> WithTestAuth(
        this WebApplicationFactory<Program> factory,
        bool allowNsfw = false,
        bool allowSfw = true) =>
        factory.WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                services.RemoveAll<ISecurityPersistence>();
                services.AddSingleton<ISecurityPersistence>(new FakeSecurityPersistence(allowSfw, allowNsfw));
                // Endpoint tests swap repositories/read services for fakes; the DB-backed
                // library-visibility guard would otherwise 404 every mutation.
                services.RemoveAll<IEntityVisibilityChecker>();
                services.AddSingleton<IEntityVisibilityChecker>(new AllVisibleEntityChecker());
                var libraryAccess = new FakeLibraryAccessStore();
                services.RemoveAll<ILibraryAccessReader>();
                services.RemoveAll<ILibraryAccessStore>();
                services.AddSingleton<ILibraryAccessReader>(libraryAccess);
                services.AddSingleton<ILibraryAccessStore>(libraryAccess);
            });
        });

    internal static HttpClient CreateAuthenticatedClient(this WebApplicationFactory<Program> factory) {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Prismedia-Api-Key", Token);
        return client;
    }

    internal sealed class AllVisibleEntityChecker : IEntityVisibilityChecker {
        public Task<bool> IsVisibleAsync(Guid entityId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    /// <summary>In-memory per-user library grants for endpoint tests.</summary>
    internal sealed class FakeLibraryAccessStore : ILibraryAccessStore {
        private readonly object _gate = new();
        private readonly HashSet<(Guid UserId, Guid RootId)> _grants = [];

        public Task<IReadOnlySet<Guid>> GetAllowedRootIdsAsync(Guid userId, CancellationToken cancellationToken) {
            lock (_gate) {
                return Task.FromResult<IReadOnlySet<Guid>>(_grants
                    .Where(grant => grant.UserId == userId)
                    .Select(grant => grant.RootId)
                    .ToHashSet());
            }
        }

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAccessByRootAsync(CancellationToken cancellationToken) {
            lock (_gate) {
                return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(_grants
                    .GroupBy(grant => grant.RootId)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<Guid>)group.Select(grant => grant.UserId).ToArray()));
            }
        }

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetAccessByUserAsync(CancellationToken cancellationToken) {
            lock (_gate) {
                return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(_grants
                    .GroupBy(grant => grant.UserId)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<Guid>)group.Select(grant => grant.RootId).ToArray()));
            }
        }

        public Task ReplaceRootAccessAsync(Guid libraryRootId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) {
            lock (_gate) {
                _grants.RemoveWhere(grant => grant.RootId == libraryRootId);
                foreach (var userId in userIds) {
                    _grants.Add((userId, libraryRootId));
                }

                return Task.CompletedTask;
            }
        }

        public Task ReplaceUserAccessAsync(Guid userId, IReadOnlyCollection<Guid> libraryRootIds, CancellationToken cancellationToken) {
            lock (_gate) {
                _grants.RemoveWhere(grant => grant.UserId == userId);
                foreach (var rootId in libraryRootIds) {
                    _grants.Add((userId, rootId));
                }

                return Task.CompletedTask;
            }
        }

        public Task GrantRootAccessAsync(Guid libraryRootId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) {
            lock (_gate) {
                foreach (var userId in userIds) {
                    _grants.Add((userId, libraryRootId));
                }

                return Task.CompletedTask;
            }
        }

        // The in-memory fake tracks no root NSFW flags; the NSFW wall is covered by the EF store's tests.
        public Task RevokeNsfwAccessAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
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
            bool? wanted = null,
            AcquisitionStatus? acquisitionStatus = null) =>
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

    /// <summary>
    /// In-memory user/session store seeded with one enabled admin (<see cref="Username"/> /
    /// <see cref="Password"/>) and one active session for <see cref="Token"/>.
    /// </summary>
    private sealed class FakeSecurityPersistence : ISecurityPersistence {
        private static readonly Guid ServerId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        internal static readonly Guid AdminUserId = Guid.Parse("88888888-8888-8888-8888-888888888888");

        private readonly object _gate = new();
        private readonly Dictionary<Guid, UserRow> _users = [];
        private readonly Dictionary<string, UserSession> _sessions = new(StringComparer.Ordinal);

        private sealed record UserRow(User User, string? PasswordHash);

        public FakeSecurityPersistence(bool allowSfw, bool allowNsfw) {
            var now = DateTimeOffset.UtcNow;
            var admin = new User(
                AdminUserId,
                Username,
                Username,
                UserRole.Admin,
                allowSfw,
                allowNsfw,
                CanCreateLibraries: true,
                Enabled: true,
                HasPassword: true,
                LastLoginAt: null,
                CreatedAt: now,
                UpdatedAt: now);
            _users[admin.Id] = new UserRow(admin, PasswordHash.Value);
            var tokenHash = UserAuthService.HashToken(Token);
            _sessions[tokenHash] = new UserSession(
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                admin.Id,
                tokenHash,
                "Tests",
                "Test Device",
                "test-device",
                "1.0",
                now,
                now,
                null);
        }

        public Task<AppSecurityState> EnsureAppSecurityAsync(CancellationToken cancellationToken) {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new AppSecurityState(1, ServerId, now, now));
        }

        public Task<IReadOnlyList<User>> ListUsersAsync(bool includeDisabled, CancellationToken cancellationToken) {
            lock (_gate) {
                return Task.FromResult<IReadOnlyList<User>>(_users.Values
                    .Select(row => row.User)
                    .Where(user => includeDisabled || user.Enabled)
                    .OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            }
        }

        public Task<User?> GetUserAsync(Guid userId, CancellationToken cancellationToken) {
            lock (_gate) {
                return Task.FromResult(_users.TryGetValue(userId, out var row) ? row.User : null);
            }
        }

        public Task<User?> FindUserByUsernameAsync(string username, CancellationToken cancellationToken) {
            lock (_gate) {
                return Task.FromResult(FindRow(username)?.User);
            }
        }

        public Task<UserWithPasswordHash?> FindUserWithPasswordHashByUsernameAsync(string username, CancellationToken cancellationToken) {
            lock (_gate) {
                var row = FindRow(username);
                return Task.FromResult(row is null ? null : new UserWithPasswordHash(row.User, row.PasswordHash));
            }
        }

        public Task<bool> AnyEnabledAdminAsync(CancellationToken cancellationToken) {
            lock (_gate) {
                return Task.FromResult(_users.Values.Any(row =>
                    row.User is { Role: UserRole.Admin, Enabled: true } && row.PasswordHash != null));
            }
        }

        public Task<bool> AnyUsersAsync(CancellationToken cancellationToken) {
            lock (_gate) {
                return Task.FromResult(_users.Count > 0);
            }
        }

        public Task<int> CountEnabledAdminsAsync(CancellationToken cancellationToken) {
            lock (_gate) {
                return Task.FromResult(_users.Values.Count(row =>
                    row.User is { Role: UserRole.Admin, Enabled: true } && row.PasswordHash != null));
            }
        }

        public Task<User> CreateUserAsync(
            string username,
            string displayName,
            string? passwordHash,
            UserRole role,
            bool allowSfw,
            bool allowNsfw,
            bool canCreateLibraries,
            bool enabled,
            CancellationToken cancellationToken) {
            lock (_gate) {
                if (FindRow(username) is not null) {
                    throw new InvalidOperationException("A user with that username already exists.");
                }

                var now = DateTimeOffset.UtcNow;
                var user = new User(
                    Guid.NewGuid(), username, displayName, role, allowSfw, allowNsfw,
                    canCreateLibraries, enabled, passwordHash != null, null, now, now);
                _users[user.Id] = new UserRow(user, passwordHash);
                return Task.FromResult(user);
            }
        }

        public Task<User?> UpdateUserAsync(
            Guid userId,
            string? username,
            string? displayName,
            UserRole? role,
            bool? allowSfw,
            bool? allowNsfw,
            bool? canCreateLibraries,
            bool? enabled,
            CancellationToken cancellationToken) {
            lock (_gate) {
                if (!_users.TryGetValue(userId, out var row)) {
                    return Task.FromResult<User?>(null);
                }

                var user = row.User with {
                    Username = username ?? row.User.Username,
                    DisplayName = displayName ?? row.User.DisplayName,
                    Role = role ?? row.User.Role,
                    AllowSfw = allowSfw ?? row.User.AllowSfw,
                    AllowNsfw = allowNsfw ?? row.User.AllowNsfw,
                    CanCreateLibraries = canCreateLibraries ?? row.User.CanCreateLibraries,
                    Enabled = enabled ?? row.User.Enabled,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                _users[userId] = row with { User = user };
                if (enabled == false) {
                    InvalidateSessions(userId, keepSessionId: null);
                }

                return Task.FromResult<User?>(user);
            }
        }

        public Task<bool> SetPasswordHashAsync(Guid userId, string passwordHash, CancellationToken cancellationToken) {
            lock (_gate) {
                if (!_users.TryGetValue(userId, out var row)) {
                    return Task.FromResult(false);
                }

                _users[userId] = row with {
                    PasswordHash = passwordHash,
                    User = row.User with { HasPassword = true, UpdatedAt = DateTimeOffset.UtcNow }
                };
                return Task.FromResult(true);
            }
        }

        public Task<bool> DeleteUserAsync(Guid userId, CancellationToken cancellationToken) {
            lock (_gate) {
                if (!_users.Remove(userId)) {
                    return Task.FromResult(false);
                }

                InvalidateSessions(userId, keepSessionId: null);
                return Task.FromResult(true);
            }
        }

        public Task<UserSession> CreateSessionAsync(
            Guid userId,
            string tokenHash,
            JellyfinClientIdentity client,
            CancellationToken cancellationToken) {
            lock (_gate) {
                var now = DateTimeOffset.UtcNow;
                var session = new UserSession(
                    Guid.NewGuid(),
                    userId,
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
        }

        public Task<UserSessionResolution?> ResolveSessionAsync(
            string tokenHash,
            TimeSpan slidingWindow,
            TimeSpan touchStaleness,
            CancellationToken cancellationToken) {
            lock (_gate) {
                if (!_sessions.TryGetValue(tokenHash, out var session) ||
                    session.InvalidatedAt is not null ||
                    !_users.TryGetValue(session.UserId, out var row) ||
                    !row.User.Enabled) {
                    return Task.FromResult<UserSessionResolution?>(null);
                }

                return Task.FromResult<UserSessionResolution?>(
                    new UserSessionResolution(session, row.User, Touched: false));
            }
        }

        public Task<IReadOnlyList<UserSession>> ListSessionsAsync(Guid userId, CancellationToken cancellationToken) {
            lock (_gate) {
                return Task.FromResult<IReadOnlyList<UserSession>>(_sessions.Values
                    .Where(session => session.UserId == userId && session.InvalidatedAt is null)
                    .OrderByDescending(session => session.LastSeenAt)
                    .ToArray());
            }
        }

        public Task<bool> InvalidateSessionAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken) {
            lock (_gate) {
                var entry = _sessions.FirstOrDefault(pair =>
                    pair.Value.Id == sessionId && pair.Value.UserId == userId && pair.Value.InvalidatedAt is null);
                if (entry.Key is null) {
                    return Task.FromResult(false);
                }

                _sessions[entry.Key] = entry.Value with { InvalidatedAt = DateTimeOffset.UtcNow };
                return Task.FromResult(true);
            }
        }

        public Task<int> InvalidateSessionsAsync(Guid userId, Guid? keepSessionId, CancellationToken cancellationToken) {
            lock (_gate) {
                return Task.FromResult(InvalidateSessions(userId, keepSessionId));
            }
        }

        public Task TouchUserLoginAsync(Guid userId, CancellationToken cancellationToken) {
            lock (_gate) {
                if (_users.TryGetValue(userId, out var row)) {
                    _users[userId] = row with { User = row.User with { LastLoginAt = DateTimeOffset.UtcNow } };
                }

                return Task.CompletedTask;
            }
        }

        private int InvalidateSessions(Guid userId, Guid? keepSessionId) {
            var now = DateTimeOffset.UtcNow;
            var invalidated = 0;
            foreach (var (key, session) in _sessions.ToArray()) {
                if (session.UserId == userId && session.InvalidatedAt is null && session.Id != keepSessionId) {
                    _sessions[key] = session with { InvalidatedAt = now };
                    invalidated++;
                }
            }

            return invalidated;
        }

        private UserRow? FindRow(string username) =>
            _users.Values.FirstOrDefault(row =>
                string.Equals(row.User.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
