using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Playback;
using Prismedia.Contracts.Playback;
using Prismedia.Contracts.Security;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Api.Tests;

public sealed class PlaybackStatisticsEndpointTests {
    private static readonly JsonSerializerOptions CodecJson =
        new(JsonSerializerDefaults.Web) { Converters = { new CodecJsonConverterFactory() } };

    [Fact]
    public async Task AdministratorCanSelectOneUsersStatistics() {
        var statistics = new CapturingPlaybackStatisticsService();
        using var factory = CreateFactory(statistics);
        using var admin = factory.CreateAuthenticatedClient();
        var selectedUserId = Guid.Parse("11111111-aaaa-4000-8000-000000000001");

        using var response = await admin.GetAsync($"/api/playback/statistics?userId={selectedUserId:D}");

        response.EnsureSuccessStatusCode();
        Assert.Equal(selectedUserId, Assert.Single(statistics.Queries).UserId);
        Assert.False(Assert.Single(statistics.Queries).AllUsers);
    }

    [Fact]
    public async Task MemberCannotSelectAnotherUserOrAllUsers() {
        var statistics = new CapturingPlaybackStatisticsService();
        using var factory = CreateFactory(statistics);
        using var admin = factory.CreateAuthenticatedClient();
        using var createResponse = await admin.PostAsJsonAsync(
            "/api/users",
            new UserCreateRequest(
                "stats-member",
                TestAuth.Password,
                Role: UserRole.Member,
                AllowSfw: true,
                AllowNsfw: false),
            CodecJson);
        createResponse.EnsureSuccessStatusCode();
        var member = await createResponse.Content.ReadFromJsonAsync<UserResponse>(CodecJson);
        Assert.NotNull(member);

        using var anonymous = factory.CreateClient();
        using var loginResponse = await anonymous.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("stats-member", TestAuth.Password));
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>(CodecJson);
        Assert.NotNull(login);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/playback/statistics?userId=11111111-aaaa-4000-8000-000000000002&allUsers=true");
        request.Headers.Authorization = new("Bearer", login.AccessToken);

        using var response = await anonymous.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var query = Assert.Single(statistics.Queries);
        Assert.Equal(member.Id, query.UserId);
        Assert.False(query.AllUsers);
    }

    [Fact]
    public async Task AdministratorCanSelectAllUsersStatistics() {
        var statistics = new CapturingPlaybackStatisticsService();
        using var factory = CreateFactory(statistics);
        using var admin = factory.CreateAuthenticatedClient();

        using var response = await admin.GetAsync("/api/playback/statistics?allUsers=true");

        response.EnsureSuccessStatusCode();
        var query = Assert.Single(statistics.Queries);
        Assert.Null(query.UserId);
        Assert.True(query.AllUsers);
    }

    private static WebApplicationFactory<Program> CreateFactory(CapturingPlaybackStatisticsService statistics) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services => {
                services.RemoveAll<IPlaybackStatisticsService>();
                services.AddSingleton<IPlaybackStatisticsService>(statistics);
            }))
            .WithTestAuth();

    private sealed class CapturingPlaybackStatisticsService : IPlaybackStatisticsService {
        public List<PlaybackStatisticsQuery> Queries { get; } = [];

        public Task<PlaybackStatisticsResponse> GetAsync(
            PlaybackStatisticsQuery query,
            CancellationToken cancellationToken) {
            Queries.Add(query);
            return Task.FromResult(new PlaybackStatisticsResponse(
                query.From,
                query.To,
                TotalEvents: 0,
                CompletedCount: 0,
                SkippedCount: 0,
                DistinctEntityCount: 0,
                TopEntities: [],
                RecentEvents: [],
                DailyEvents: []));
        }
    }
}
