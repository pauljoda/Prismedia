using Prismedia.Api.Security;
using Prismedia.Application.Security;
using Prismedia.Contracts.Security;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>Admin-only user account management: CRUD, password resets, and role/permission flags.</summary>
public static class UserEndpoints {
    public static RouteGroupBuilder MapUserEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/users")
            .WithTags("Users")
            .RequireAdmin();

        group.MapGet("", async (
            UserAdminService users,
            CancellationToken cancellationToken) => {
            var items = await users.ListUsersAsync(cancellationToken);
            return Results.Ok(new UsersResponse(items.Select(user => user.ToResponse()).ToArray()));
        })
            .WithName("ListUsers")
            .WithSummary("Lists all user accounts.")
            .Produces<UsersResponse>();

        group.MapGet("/{userId:guid}", async (
            Guid userId,
            UserAdminService users,
            CancellationToken cancellationToken) =>
            await users.GetUserAsync(userId, cancellationToken) is { } user
                ? Results.Ok(user.ToResponse())
                : UserNotFound(userId))
            .WithName("GetUser")
            .WithSummary("Gets one user account.")
            .Produces<UserResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("", async (
            UserCreateRequest request,
            UserAdminService users,
            CancellationToken cancellationToken) => {
            try {
                var user = await users.CreateUserAsync(
                    request.Username,
                    request.DisplayName,
                    request.Password,
                    request.Role,
                    request.AllowSfw,
                    request.AllowNsfw,
                    request.CanCreateLibraries,
                    request.Enabled,
                    cancellationToken);
                return Results.Ok(user.ToResponse());
            } catch (SecurityProblemException ex) {
                return ex.ToResult();
            }
        })
            .WithName("CreateUser")
            .WithSummary("Creates a user account.")
            .Produces<UserResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPatch("/{userId:guid}", async (
            Guid userId,
            UserUpdateRequest request,
            UserAdminService users,
            CancellationToken cancellationToken) => {
            try {
                var user = await users.UpdateUserAsync(
                    userId,
                    request.Username,
                    request.DisplayName,
                    request.Role,
                    request.AllowSfw,
                    request.AllowNsfw,
                    request.CanCreateLibraries,
                    request.Enabled,
                    cancellationToken);
                return user is null ? UserNotFound(userId) : Results.Ok(user.ToResponse());
            } catch (SecurityProblemException ex) {
                return ex.ToResult();
            }
        })
            .WithName("UpdateUser")
            .WithSummary("Updates a user account; the last enabled admin cannot be demoted or disabled.")
            .Produces<UserResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound)
            .Produces<ApiProblem>(StatusCodes.Status409Conflict);

        group.MapPost("/{userId:guid}/password", async (
            Guid userId,
            AdminSetPasswordRequest request,
            UserAdminService users,
            CancellationToken cancellationToken) => {
            try {
                return await users.SetPasswordAsync(userId, request.NewPassword, cancellationToken)
                    ? Results.NoContent()
                    : UserNotFound(userId);
            } catch (SecurityProblemException ex) {
                return ex.ToResult();
            }
        })
            .WithName("SetUserPassword")
            .WithSummary("Resets a user's password and signs them out everywhere.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapDelete("/{userId:guid}", async (
            Guid userId,
            UserAdminService users,
            CancellationToken cancellationToken) => {
            try {
                return await users.DeleteUserAsync(userId, cancellationToken)
                    ? Results.NoContent()
                    : UserNotFound(userId);
            } catch (SecurityProblemException ex) {
                return ex.ToResult();
            }
        })
            .WithName("DeleteUser")
            .WithSummary("Deletes a user account; the last enabled admin cannot be deleted.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound)
            .Produces<ApiProblem>(StatusCodes.Status409Conflict);

        return group;
    }

    private static IResult UserNotFound(Guid userId) =>
        Results.NotFound(new ApiProblem(ApiProblemCodes.UserNotFound, $"User '{userId}' was not found."));
}
