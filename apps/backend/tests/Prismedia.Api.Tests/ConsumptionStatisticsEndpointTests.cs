using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prismedia.Application.Consumption;
using Prismedia.Contracts.Consumption;
using Prismedia.Contracts.Security;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Serialization;

namespace Prismedia.Api.Tests;

public sealed class ConsumptionStatisticsEndpointTests {
    private static readonly JsonSerializerOptions CodecJson =
        new(JsonSerializerDefaults.Web) { Converters = { new CodecJsonConverterFactory() } };

    [Fact]
    public async Task AdministratorCanSelectOneUsersStatistics() {
        var statistics = new CapturingConsumptionStatisticsService();
        using var factory = CreateFactory(statistics);
        using var admin = factory.CreateAuthenticatedClient();
        var selectedUserId = Guid.Parse("11111111-aaaa-4000-8000-000000000001");

        using var response = await admin.GetAsync($"/api/consumption/statistics?userId={selectedUserId:D}");

        response.EnsureSuccessStatusCode();
        Assert.Equal(selectedUserId, Assert.Single(statistics.Queries).UserId);
        Assert.False(Assert.Single(statistics.Queries).AllUsers);
    }

    [Fact]
    public async Task MemberCannotSelectAnotherUserOrAllUsers() {
        var statistics = new CapturingConsumptionStatisticsService();
        using var factory = CreateFactory(statistics);
        using var admin = factory.CreateAuthenticatedClient();
        using var createResponse = await admin.PostAsJsonAsync(
            "/api/users",
            new UserCreateRequest(
                "stats-member",
                TestAuth.Password,
                Role: UserRole.Member,
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
            "/api/consumption/statistics?userId=11111111-aaaa-4000-8000-000000000002&allUsers=true");
        request.Headers.Authorization = new("Bearer", login.AccessToken);

        using var response = await anonymous.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var query = Assert.Single(statistics.Queries);
        Assert.Equal(member.Id, query.UserId);
        Assert.False(query.AllUsers);
    }

    [Fact]
    public async Task AdministratorCanSelectAllUsersStatistics() {
        var statistics = new CapturingConsumptionStatisticsService();
        using var factory = CreateFactory(statistics);
        using var admin = factory.CreateAuthenticatedClient();

        using var response = await admin.GetAsync("/api/consumption/statistics?allUsers=true");

        response.EnsureSuccessStatusCode();
        var query = Assert.Single(statistics.Queries);
        Assert.Null(query.UserId);
        Assert.True(query.AllUsers);
    }

    [Fact]
    public async Task CallerLocalOffsetFlowsIntoTheStatisticsQuery() {
        var statistics = new CapturingConsumptionStatisticsService();
        using var factory = CreateFactory(statistics);
        using var admin = factory.CreateAuthenticatedClient();

        using var response = await admin.GetAsync("/api/consumption/statistics?utcOffsetMinutes=-300");

        response.EnsureSuccessStatusCode();
        Assert.Equal(-300, Assert.Single(statistics.Queries).UtcOffsetMinutes);
    }

    [Fact]
    public async Task RemovedPlaybackStatisticsRouteIsNotExposed() {
        var statistics = new CapturingConsumptionStatisticsService();
        using var factory = CreateFactory(statistics);
        using var admin = factory.CreateAuthenticatedClient();

        using var response = await admin.GetAsync("/api/playback/statistics");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(statistics.Queries);
    }

    private static WebApplicationFactory<Program> CreateFactory(CapturingConsumptionStatisticsService statistics) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services => {
                services.RemoveAll<IConsumptionStatisticsService>();
                services.AddSingleton<IConsumptionStatisticsService>(statistics);
            }))
            .WithTestAuth();

    private sealed class CapturingConsumptionStatisticsService : IConsumptionStatisticsService {
        public List<ConsumptionStatisticsQuery> Queries { get; } = [];

        public Task<ConsumptionStatisticsResponse> GetAsync(
            ConsumptionStatisticsQuery query,
            CancellationToken cancellationToken) {
            Queries.Add(query);
            return Task.FromResult(new ConsumptionStatisticsResponse(
                query.From,
                query.To,
                TotalEvents: 0,
                AccessedCount: 0,
                CompletedCount: 0,
                SkippedCount: 0,
                DistinctEntityCount: 0,
                ActiveSeconds: 0,
                ViewingSeconds: 0,
                ReadingSeconds: 0,
                ListeningSeconds: 0,
                TopEntities: [],
                RecentEvents: [],
                DailyEvents: [],
                KindBreakdown: [],
                Rhythm: []));
        }
    }
}
