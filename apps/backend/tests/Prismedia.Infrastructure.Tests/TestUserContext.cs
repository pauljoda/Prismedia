using Prismedia.Application.Security;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Tests;

/// <summary>
/// Test doubles for the request-scoped user context. Most suites predate multi-user and
/// exercise single-user semantics, so <see cref="Admin"/> (unrestricted, fixed id) is the
/// drop-in default; member contexts take an explicit allowed-library set.
/// </summary>
internal static class TestUserContext {
    internal static readonly Guid UserId = Guid.Parse("faceb00c-0000-4000-8000-000000000001");

    /// <summary>Unrestricted admin context with a stable user id.</summary>
    internal static ICurrentUserContext Admin() => Context(UserRole.Admin);

    /// <summary>Unrestricted admin context with an explicit user id.</summary>
    internal static ICurrentUserContext Admin(Guid userId) => Context(UserRole.Admin, userId: userId);

    /// <summary>Member context restricted to the given library roots.</summary>
    internal static ICurrentUserContext Member(params Guid[] allowedRootIds) =>
        Context(UserRole.Member, allowedRootIds.ToHashSet());

    /// <summary>Member context with an explicit user id and unrestricted test-library access.</summary>
    internal static ICurrentUserContext MemberAs(Guid userId) => Context(UserRole.Member, userId: userId);

    /// <summary>Unauthenticated context (no user, no engagement state).</summary>
    internal static ICurrentUserContext Anonymous() => new CurrentUserContextHolder();

    private static ICurrentUserContext Context(
        UserRole role,
        IReadOnlySet<Guid>? allowedRootIds = null,
        Guid? userId = null) {
        var holder = new CurrentUserContextHolder(
            allowedRootIds is null ? null : new FixedAccess(allowedRootIds));
        var now = DateTimeOffset.UtcNow;
        holder.Set(
            new User(
                userId ?? UserId,
                "test-user",
                "Test User",
                role,
                AllowNsfw: true,
                CanCreateLibraries: role == UserRole.Admin,
                CanRequestContent: role == UserRole.Admin,
                Enabled: true,
                HasPassword: true,
                LastLoginAt: null,
                CreatedAt: now,
                UpdatedAt: now),
            sessionId: Guid.NewGuid());
        return holder;
    }

    private sealed class FixedAccess(IReadOnlySet<Guid> allowed) : ILibraryAccessReader {
        public Task<IReadOnlySet<Guid>> GetAllowedRootIdsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(allowed);
    }
}
