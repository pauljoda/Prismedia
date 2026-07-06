using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Prismedia.Infrastructure.Serialization;
using Prismedia.Contracts.Security;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Tests;

public sealed class AuthEndpointTests {
    // Codec enums (UserResponse.Role) serialize as their string code; the client needs the
    // same converter to round-trip the typed DTOs.
    private static readonly JsonSerializerOptions CodecJson =
        new(JsonSerializerDefaults.Web) { Converters = { new CodecJsonConverterFactory() } };

    [Fact]
    public async Task SetupStatusIsPublicAndCompleteWhenAdminExists() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<SetupStatusResponse>("/api/auth/setup-status");

        Assert.NotNull(status);
        Assert.False(status.NeedsSetup);
        Assert.True(status.HasUsers);
    }

    [Fact]
    public async Task SetupIsRejectedOnceAnAdminExists() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/setup",
            new CreateFirstAdminRequest("intruder", "intruder-password"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();
        Assert.Equal(ApiProblemCodes.SetupAlreadyCompleted, problem!.Code);
    }

    [Fact]
    public async Task LoginIssuesSessionCookieAndBearerToken() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(TestAuth.Username, TestAuth.Password));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(CodecJson);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        Assert.Equal(UserRole.Admin, body.User.Role);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("prismedia-session=", StringComparison.Ordinal));
        Assert.Contains("HttpOnly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Lax", cookie, StringComparison.OrdinalIgnoreCase);

        // The returned bearer token authenticates API calls (native client path).
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new("Bearer", body.AccessToken);
        using var meResponse = await client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public async Task LoginRejectsWrongPasswordWithInvalidCredentials() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(TestAuth.Username, "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>();
        Assert.Equal(ApiProblemCodes.InvalidCredentials, problem!.Code);
    }

    [Fact]
    public async Task RepeatedFailedLoginsAreThrottled() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        HttpResponseMessage? response = null;

        for (var i = 0; i < 9; i++) {
            response?.Dispose();
            response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest(TestAuth.Username, $"bad-password-{i}"));
        }

        using (response) {
            Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
        }
    }

    [Fact]
    public async Task LogoutInvalidatesTheSession() {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(TestAuth.Username, TestAuth.Password));
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(CodecJson);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Authorization = new("Bearer", login!.AccessToken);
        using var logoutResponse = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new("Bearer", login.AccessToken);
        using var meResponse = await client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    [Fact]
    public async Task OwnSessionsCanBeListedAndRevoked() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();
        using var otherLogin = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(TestAuth.Username, TestAuth.Password, Client: "Other Device"));
        otherLogin.EnsureSuccessStatusCode();

        var sessions = await client.GetFromJsonAsync<UserSessionsResponse>("/api/auth/sessions");
        Assert.NotNull(sessions);
        Assert.True(sessions.Items.Count >= 2);
        Assert.Contains(sessions.Items, session => session.IsCurrent);
        var other = sessions.Items.First(session => !session.IsCurrent);

        using var revokeResponse = await client.DeleteAsync($"/api/auth/sessions/{other.Id:D}");
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);
        var after = await client.GetFromJsonAsync<UserSessionsResponse>("/api/auth/sessions");
        Assert.DoesNotContain(after!.Items, session => session.Id == other.Id);
    }

    [Fact]
    public async Task ProfileDisplayNameCanBeUpdated() {
        using var factory = CreateFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PatchAsJsonAsync(
            "/api/auth/me",
            new UpdateOwnProfileRequest("The Librarian"));

        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<UserResponse>(CodecJson);
        Assert.Equal("The Librarian", user!.DisplayName);
    }

    [Fact]
    public async Task UserAdminCrudWorksAndEnforcesGuards() {
        using var factory = CreateFactory();
        using var admin = factory.CreateAuthenticatedClient();

        // Create a member.
        using var createResponse = await admin.PostAsJsonAsync(
            "/api/users",
            new UserCreateRequest("alice", "alice-password", "Alice", AllowNsfw: false, CanCreateLibraries: true), CodecJson);
        createResponse.EnsureSuccessStatusCode();
        var alice = await createResponse.Content.ReadFromJsonAsync<UserResponse>(CodecJson);
        Assert.Equal(UserRole.Member, alice!.Role);
        Assert.True(alice.CanCreateLibraries);

        // Members cannot reach user administration.
        using var memberClient = factory.CreateClient();
        using var memberLogin = await memberClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("alice", "alice-password"));
        var memberAuth = await memberLogin.Content.ReadFromJsonAsync<LoginResponse>(CodecJson);
        using var forbiddenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        forbiddenRequest.Headers.Authorization = new("Bearer", memberAuth!.AccessToken);
        using var forbiddenResponse = await memberClient.SendAsync(forbiddenRequest);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        var forbiddenProblem = await forbiddenResponse.Content.ReadFromJsonAsync<ApiProblem>();
        Assert.Equal(ApiProblemCodes.AdminRequired, forbiddenProblem!.Code);

        // Admin resets the member's password; their session dies.
        using var resetResponse = await admin.PostAsJsonAsync(
            $"/api/users/{alice.Id:D}/password",
            new AdminSetPasswordRequest("alice-new-password"));
        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);
        using var deadRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        deadRequest.Headers.Authorization = new("Bearer", memberAuth.AccessToken);
        using var deadResponse = await memberClient.SendAsync(deadRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, deadResponse.StatusCode);

        // The last enabled admin cannot be demoted or deleted.
        var users = await admin.GetFromJsonAsync<UsersResponse>("/api/users", CodecJson);
        var adminUser = users!.Items.Single(user => user.Role == UserRole.Admin);
        using var demoteResponse = await admin.PatchAsJsonAsync(
            $"/api/users/{adminUser.Id:D}",
            new UserUpdateRequest(Role: UserRole.Member), CodecJson);
        Assert.Equal(HttpStatusCode.Conflict, demoteResponse.StatusCode);
        using var deleteAdminResponse = await admin.DeleteAsync($"/api/users/{adminUser.Id:D}");
        Assert.Equal(HttpStatusCode.Conflict, deleteAdminResponse.StatusCode);

        // Deleting the member works.
        using var deleteResponse = await admin.DeleteAsync($"/api/users/{alice.Id:D}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Duplicate usernames are rejected.
        using var duplicateResponse = await admin.PostAsJsonAsync(
            "/api/users",
            new UserCreateRequest(TestAuth.Username, "whatever-password"), CodecJson);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);

        // Weak passwords are rejected.
        using var weakResponse = await admin.PostAsJsonAsync(
            "/api/users",
            new UserCreateRequest("bob", "short"), CodecJson);
        Assert.Equal(HttpStatusCode.BadRequest, weakResponse.StatusCode);
        var weakProblem = await weakResponse.Content.ReadFromJsonAsync<ApiProblem>();
        Assert.Equal(ApiProblemCodes.PasswordInvalid, weakProblem!.Code);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithTestAuth();
}
