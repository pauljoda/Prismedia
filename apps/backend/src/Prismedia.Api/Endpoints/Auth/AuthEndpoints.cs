using Prismedia.Api.Security;
using Prismedia.Application.Security;
using Prismedia.Contracts.Security;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>
/// Session-based authentication for the web portal and native clients: first-run setup,
/// login/logout, current-user info, and self-service password/profile/session management.
/// The same session tokens authenticate Jellyfin and OPDS traffic.
/// </summary>
public static class AuthEndpoints {
    private const string WebClientName = "Prismedia Web";

    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/auth")
            .WithTags("Auth");

        group.MapGet("/setup-status", async (
            UserAuthService auth,
            CancellationToken cancellationToken) => {
            var status = await auth.GetSetupStatusAsync(cancellationToken);
            return Results.Ok(new SetupStatusResponse(status.NeedsSetup, status.HasUsers));
        })
            .WithName("GetSetupStatus")
            .WithSummary("Reports whether first-run setup (creating an admin) is required.")
            .Produces<SetupStatusResponse>();

        group.MapPost("/setup", async (
            CreateFirstAdminRequest request,
            HttpContext httpContext,
            UserAuthService auth,
            ILogger<UserAuthService> logger,
            CancellationToken cancellationToken) => {
            try {
                var result = await auth.CreateFirstAdminAsync(
                    request.Username,
                    request.DisplayName,
                    request.Password,
                    ClientIdentity(httpContext),
                    PrismediaAuthentication.BucketFor(httpContext),
                    cancellationToken);
                if (result.IsThrottled) {
                    return ThrottledResult();
                }

                logger.LogWarning(
                    "SETUP: first admin account '{Username}' was created from {RemoteIp}.",
                    result.User!.Username,
                    httpContext.Connection.RemoteIpAddress);
                return SignedInResult(httpContext, result);
            } catch (SecurityProblemException ex) {
                return ex.ToResult();
            }
        })
            .WithName("CompleteSetup")
            .WithSummary("Creates the first admin account and signs it in.")
            .Produces<LoginResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status409Conflict);

        group.MapPost("/login", async (
            LoginRequest request,
            HttpContext httpContext,
            UserAuthService auth,
            CancellationToken cancellationToken) => {
            var result = await auth.AuthenticateAsync(
                request.Username,
                request.Password,
                ClientIdentity(httpContext, request),
                PrismediaAuthentication.BucketFor(httpContext, request.Username),
                cancellationToken);
            if (result.IsThrottled) {
                return ThrottledResult();
            }

            if (!result.Succeeded) {
                return Results.Json(
                    new ApiProblem(ApiProblemCodes.InvalidCredentials, "Invalid username or password."),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            return SignedInResult(httpContext, result);
        })
            .WithName("Login")
            .WithSummary("Signs in with username and password, issuing a session cookie and bearer token.")
            .Produces<LoginResponse>()
            .Produces<ApiProblem>(StatusCodes.Status401Unauthorized)
            .Produces<ApiProblem>(StatusCodes.Status429TooManyRequests);

        group.MapPost("/logout", async (
            HttpContext httpContext,
            UserAuthService auth,
            CancellationToken cancellationToken) => {
            if (httpContext.GetPrismediaAuth() is { Session: { } session, User: { } user }) {
                await auth.LogoutAsync(session.Id, user.Id, cancellationToken);
            }

            httpContext.ExpireSessionCookie();
            return Results.NoContent();
        })
            .WithName("Logout")
            .WithSummary("Invalidates the current session and clears the session cookie.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", (HttpContext httpContext) =>
            httpContext.GetCurrentUser() is { } user
                ? Results.Ok(user.ToResponse())
                : Results.Json(
                    new ApiProblem(ApiProblemCodes.AuthenticationRequired, "Authentication is required."),
                    statusCode: StatusCodes.Status401Unauthorized))
            .WithName("GetCurrentUser")
            .WithSummary("Gets the signed-in user.")
            .Produces<UserResponse>()
            .Produces<ApiProblem>(StatusCodes.Status401Unauthorized);

        group.MapPatch("/me", async (
            UpdateOwnProfileRequest request,
            HttpContext httpContext,
            UserAuthService auth,
            CancellationToken cancellationToken) => {
            var current = httpContext.GetCurrentUser()!;
            try {
                var updated = await auth.UpdateOwnDisplayNameAsync(current.Id, request.DisplayName, cancellationToken);
                return updated is null
                    ? Results.NotFound(new ApiProblem(ApiProblemCodes.UserNotFound, "The account no longer exists."))
                    : Results.Ok(updated.ToResponse());
            } catch (SecurityProblemException ex) {
                return ex.ToResult();
            }
        })
            .WithName("UpdateCurrentUser")
            .WithSummary("Updates the signed-in user's profile.")
            .Produces<UserResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPost("/password", async (
            ChangeOwnPasswordRequest request,
            HttpContext httpContext,
            UserAuthService auth,
            CancellationToken cancellationToken) => {
            var auth0 = httpContext.GetPrismediaAuth()!;
            try {
                var changed = await auth.ChangeOwnPasswordAsync(
                    auth0.User.Id,
                    auth0.Session?.Id ?? Guid.Empty,
                    request.CurrentPassword,
                    request.NewPassword,
                    cancellationToken);
                return changed
                    ? Results.NoContent()
                    : Results.BadRequest(new ApiProblem(ApiProblemCodes.InvalidCredentials, "The current password is incorrect."));
            } catch (SecurityProblemException ex) {
                return ex.ToResult();
            }
        })
            .WithName("ChangeOwnPassword")
            .WithSummary("Changes the signed-in user's password and signs out other sessions.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapGet("/sessions", async (
            HttpContext httpContext,
            UserAuthService auth,
            CancellationToken cancellationToken) => {
            var auth0 = httpContext.GetPrismediaAuth()!;
            var sessions = await auth.ListOwnSessionsAsync(auth0.User.Id, cancellationToken);
            return Results.Ok(new UserSessionsResponse(sessions
                .Select(session => new UserSessionResponse(
                    session.Id,
                    session.Client,
                    session.DeviceName,
                    session.DeviceId,
                    session.ApplicationVersion,
                    session.CreatedAt,
                    session.LastSeenAt,
                    IsCurrent: session.Id == auth0.Session?.Id))
                .ToArray()));
        })
            .WithName("ListOwnSessions")
            .WithSummary("Lists the signed-in user's active sessions (devices).")
            .Produces<UserSessionsResponse>();

        group.MapDelete("/sessions/{sessionId:guid}", async (
            Guid sessionId,
            HttpContext httpContext,
            UserAuthService auth,
            CancellationToken cancellationToken) => {
            var auth0 = httpContext.GetPrismediaAuth()!;
            if (!await auth.RevokeOwnSessionAsync(auth0.User.Id, sessionId, cancellationToken)) {
                return Results.NotFound(new ApiProblem(ApiProblemCodes.SessionNotFound, $"Session '{sessionId}' was not found."));
            }

            if (sessionId == auth0.Session?.Id) {
                httpContext.ExpireSessionCookie();
            }

            return Results.NoContent();
        })
            .WithName("RevokeOwnSession")
            .WithSummary("Revokes one of the signed-in user's sessions.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    private static IResult SignedInResult(HttpContext httpContext, UserAuthenticationResult result) {
        httpContext.AppendSessionCookie(result.AccessToken!);
        httpContext.ExpireLegacyApiKeyCookie();
        return Results.Ok(new LoginResponse(result.AccessToken!, result.User!.ToResponse()));
    }

    private static IResult ThrottledResult() =>
        Results.Json(
            new ApiProblem(ApiProblemCodes.AuthRateLimited, "Too many failed authentication attempts."),
            statusCode: StatusCodes.Status429TooManyRequests);

    private static JellyfinClientIdentity ClientIdentity(HttpContext httpContext, LoginRequest? request = null) {
        var header = httpContext.Request.GetJellyfinClientIdentity();
        return new JellyfinClientIdentity(
            request?.Client ?? header.Client ?? WebClientName,
            request?.DeviceName ?? header.DeviceName,
            request?.DeviceId ?? header.DeviceId,
            header.ApplicationVersion);
    }
}
