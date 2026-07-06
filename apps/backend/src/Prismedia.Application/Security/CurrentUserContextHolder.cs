using Prismedia.Domain.Entities;

namespace Prismedia.Application.Security;

/// <summary>
/// Scoped mutable implementation of <see cref="ICurrentUserContext"/>. The API
/// authentication middleware calls <see cref="Set"/> once per authenticated request;
/// the worker calls <see cref="SetSystem"/> from a permanent root-scope instance.
/// </summary>
public sealed class CurrentUserContextHolder : ICurrentUserContext {
    private readonly ILibraryAccessReader? _libraryAccess;
    private User? _user;
    private bool _allowedLoaded;
    private IReadOnlySet<Guid>? _allowedRootIds;

    public CurrentUserContextHolder(ILibraryAccessReader? libraryAccess = null) {
        _libraryAccess = libraryAccess;
    }

    /// <summary>Binds the resolved user and session to this request scope.</summary>
    public void Set(User user, Guid sessionId) {
        _user = user;
        SessionId = sessionId;
        _allowedLoaded = false;
        _allowedRootIds = null;
    }

    /// <summary>Marks this scope as system execution (worker/startup): unrestricted, no user.</summary>
    public void SetSystem() => IsSystem = true;

    /// <inheritdoc />
    public bool IsAuthenticated => _user is not null;

    /// <inheritdoc />
    public bool IsSystem { get; private set; }

    /// <inheritdoc />
    public Guid UserId => _user?.Id ?? Guid.Empty;

    /// <inheritdoc />
    public Guid SessionId { get; private set; }

    /// <inheritdoc />
    public string Username => _user?.Username ?? string.Empty;

    /// <inheritdoc />
    public UserRole Role => _user?.Role ?? UserRole.Member;

    /// <inheritdoc />
    public bool IsAdmin => _user?.Role == UserRole.Admin;

    /// <inheritdoc />
    public bool AllowSfw => _user?.AllowSfw ?? true;

    /// <inheritdoc />
    public bool AllowNsfw => _user?.AllowNsfw ?? false;

    /// <inheritdoc />
    public bool CanCreateLibraries => IsAdmin || (_user?.CanCreateLibraries ?? false);

    /// <inheritdoc />
    public async ValueTask<IReadOnlySet<Guid>?> GetAllowedLibraryRootIdsAsync(CancellationToken cancellationToken) {
        if (IsSystem || _user is null || _user.Role == UserRole.Admin || _libraryAccess is null) {
            return null;
        }

        if (!_allowedLoaded) {
            _allowedRootIds = await _libraryAccess.GetAllowedRootIdsAsync(_user.Id, cancellationToken);
            _allowedLoaded = true;
        }

        return _allowedRootIds;
    }
}
