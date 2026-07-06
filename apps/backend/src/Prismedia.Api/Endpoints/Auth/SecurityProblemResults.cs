using Prismedia.Application.Security;
using Prismedia.Contracts.Security;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>Shared HTTP mapping for auth/user domain-rule violations and DTO projection.</summary>
internal static class SecurityProblemResults {
    /// <summary>Maps a <see cref="SecurityProblemException"/> to its HTTP status by problem code.</summary>
    internal static IResult ToResult(this SecurityProblemException exception) =>
        Results.Json(new ApiProblem(exception.Code, exception.Message), statusCode: exception.Code switch {
            ApiProblemCodes.SetupAlreadyCompleted or ApiProblemCodes.LastAdminRequired => StatusCodes.Status409Conflict,
            ApiProblemCodes.UserNotFound or ApiProblemCodes.SessionNotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        });

    /// <summary>Projects an application user onto the API user DTO.</summary>
    internal static UserResponse ToResponse(this User user, IReadOnlyList<Guid>? libraryRootIds = null) =>
        new(
            user.Id,
            user.Username,
            user.DisplayName,
            user.Role,
            user.AllowSfw,
            user.AllowNsfw,
            user.CanCreateLibraries,
            user.Enabled,
            user.LastLoginAt,
            user.CreatedAt,
            user.UpdatedAt,
            libraryRootIds);
}
