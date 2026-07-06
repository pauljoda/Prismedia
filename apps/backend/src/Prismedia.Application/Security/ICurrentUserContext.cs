using Prismedia.Domain.Entities;

namespace Prismedia.Application.Security;

/// <summary>
/// Request-scoped identity of the authenticated user. The API authentication middleware
/// populates it before endpoints run; it stays empty for unauthenticated public routes.
/// Background processes (the worker) run with a permanent system context that bypasses
/// per-user restrictions without carrying a user id.
/// </summary>
public interface ICurrentUserContext {
    /// <summary>True when a user session was resolved for this request.</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// True for background/system execution (worker, startup): unrestricted library
    /// visibility and no per-user engagement state.
    /// </summary>
    bool IsSystem { get; }

    /// <summary>Authenticated user id, or <see cref="Guid.Empty"/> when unauthenticated.</summary>
    Guid UserId { get; }

    /// <summary>Resolved session id, or <see cref="Guid.Empty"/> when unauthenticated.</summary>
    Guid SessionId { get; }

    /// <summary>Authenticated username, or empty when unauthenticated.</summary>
    string Username { get; }

    /// <summary>Role of the authenticated user; <see cref="UserRole.Member"/> when unauthenticated.</summary>
    UserRole Role { get; }

    /// <summary>True when the authenticated user is an admin.</summary>
    bool IsAdmin { get; }

    /// <summary>Whether the user may see safe-for-work content.</summary>
    bool AllowSfw { get; }

    /// <summary>Server-enforced NSFW ceiling: false means NSFW content is never visible.</summary>
    bool AllowNsfw { get; }

    /// <summary>Whether a member may create library roots (admins always can).</summary>
    bool CanCreateLibraries { get; }

    /// <summary>
    /// Library roots visible to this user, or null when unrestricted (admins, system
    /// context). Memoized per request; members cost at most one indexed read.
    /// </summary>
    ValueTask<IReadOnlySet<Guid>?> GetAllowedLibraryRootIdsAsync(CancellationToken cancellationToken);
}
