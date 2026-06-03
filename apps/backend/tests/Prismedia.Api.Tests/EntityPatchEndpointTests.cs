using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Entities;
using Prismedia.Domain.Capabilities;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Media;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Plugins;

namespace Prismedia.Api.Tests;

public sealed class EntityPatchEndpointTests {
    private static readonly Guid EntityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid VolumeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ChapterId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task KindGuardedEntityPatchAppliesMetadataAndReturnsUpdatedCard() {
        using var factory = CreateFactory(EntityMetadataPatchResult.Applied);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PatchAsJsonAsync(
            $"/api/entities/video/{EntityId}",
            Request("Updated Title"));
        var card = await response.Content.ReadFromJsonAsync<EntityCard>();
        var patcher = factory.Services.GetRequiredService<FakeEntityMetadataPatchService>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(card);
        Assert.Equal("Video Title", card.Title);
        Assert.Equal(EntityId, patcher.EntityId);
        Assert.Equal("video", patcher.ExpectedKind);
    }

    [Fact]
    public async Task KindGuardedEntityPatchReturnsNotFoundForKindMismatch() {
        using var factory = CreateFactory(EntityMetadataPatchResult.KindMismatch);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PatchAsJsonAsync(
            $"/api/entities/video-series/{EntityId}",
            Request("Wrong Kind"));
        var patcher = factory.Services.GetRequiredService<FakeEntityMetadataPatchService>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("video-series", patcher.ExpectedKind);
    }

    [Fact]
    public async Task DomainEntityPatchRoutesDelegateWithDomainKind() {
        using var factory = CreateFactory(EntityMetadataPatchResult.Applied);
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PatchAsJsonAsync(
            $"/api/videos/{EntityId}",
            Request("Video Title"));
        var patcher = factory.Services.GetRequiredService<FakeEntityMetadataPatchService>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video", patcher.ExpectedKind);
    }

    [Fact]
    public async Task EntityProgressPatchStoresSharedProgressCapability() {
        using var factory = CreateProgressFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PatchAsJsonAsync(
            $"/api/entities/{EntityId}/progress",
            new EntityProgressUpdateRequest(ChapterId, "page", 7, 24, "webtoon", Completed: null));
        var repository = factory.Services.GetRequiredService<FakeEntityWriteRepository>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChapterId, repository.SavedEntity?.Progress?.CurrentEntityId);
        Assert.Equal("page", repository.SavedEntity?.Progress?.Unit);
        Assert.Equal(7, repository.SavedEntity?.Progress?.Index);
        Assert.Equal(24, repository.SavedEntity?.Progress?.Total);
        Assert.Equal("webtoon", repository.SavedEntity?.Progress?.Mode);
        Assert.Null(repository.SavedEntity?.Progress?.CompletedAt);
    }

    [Fact]
    public async Task EntityRatingPatchStoresSharedRatingCapability() {
        using var factory = CreateProgressFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PatchAsJsonAsync(
            $"/api/entities/{EntityId}/rating",
            new RatingUpdateRequest(4));
        var repository = factory.Services.GetRequiredService<FakeEntityWriteRepository>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(4, repository.SavedEntity?.RatingValue);
    }

    [Fact]
    public async Task EntityProgressPatchMarksProgressComplete() {
        using var factory = CreateProgressFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PatchAsJsonAsync(
            $"/api/entities/{EntityId}/progress",
            new EntityProgressUpdateRequest(ChapterId, "page", 23, 24, "paged", Completed: true));
        var repository = factory.Services.GetRequiredService<FakeEntityWriteRepository>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ChapterId, repository.SavedEntity?.Progress?.CurrentEntityId);
        Assert.Equal(23, repository.SavedEntity?.Progress?.Index);
        Assert.NotNull(repository.SavedEntity?.Progress?.CompletedAt);
    }

    [Fact]
    public async Task EntityProgressPatchResetReturnsToStartAndClearsCompletion() {
        using var factory = CreateProgressFactory();
        using var client = factory.CreateAuthenticatedClient();
        var repository = factory.Services.GetRequiredService<FakeEntityWriteRepository>();

        using var complete = await client.PatchAsJsonAsync(
            $"/api/entities/{EntityId}/progress",
            new EntityProgressUpdateRequest(ChapterId, "page", 23, 24, "paged", Completed: true));
        Assert.NotNull(repository.SavedEntity?.Progress?.CompletedAt);

        // Start over resets to the beginning and clears completion despite the forward-only guard.
        using var response = await client.PatchAsJsonAsync(
            $"/api/entities/{EntityId}/progress",
            new EntityProgressUpdateRequest(ChapterId, "page", 0, 24, "paged", Completed: null, Reset: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, repository.SavedEntity?.Progress?.Index);
        Assert.Null(repository.SavedEntity?.Progress?.CompletedAt);
    }

    [Fact]
    public async Task EntityProgressPatchMarkUnreadClearsCompletionWithoutMovingTheCursor() {
        using var factory = CreateProgressFactory();
        using var client = factory.CreateAuthenticatedClient();
        var repository = factory.Services.GetRequiredService<FakeEntityWriteRepository>();

        using var complete = await client.PatchAsJsonAsync(
            $"/api/entities/{EntityId}/progress",
            new EntityProgressUpdateRequest(ChapterId, "page", 23, 24, "paged", Completed: true));
        Assert.NotNull(repository.SavedEntity?.Progress?.CompletedAt);

        // Mark unread clears completion in place, leaving the page position untouched.
        using var response = await client.PatchAsJsonAsync(
            $"/api/entities/{EntityId}/progress",
            new EntityProgressUpdateRequest(ChapterId, "page", 23, 24, "paged", Completed: false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(repository.SavedEntity?.Progress?.CompletedAt);
        Assert.Equal(23, repository.SavedEntity?.Progress?.Index);
    }

    [Fact]
    public async Task EntityProgressPatchOnBookChildStoresProgressOnBookParent() {
        using var factory = CreateProgressFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PatchAsJsonAsync(
            $"/api/entities/{ChapterId}/progress",
            new EntityProgressUpdateRequest(ChapterId, "page", 2, 16, "paged", Completed: null));
        var repository = factory.Services.GetRequiredService<FakeEntityWriteRepository>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(EntityId, repository.SavedEntity?.Id);
        Assert.Equal(ChapterId, repository.SavedEntity?.Progress?.CurrentEntityId);
        Assert.Equal(2, repository.SavedEntity?.Progress?.Index);
    }

    [Fact]
    public async Task EntityProgressPatchResolvesBookOwnerWithoutFullHydration() {
        using var factory = CreateProgressFactory();
        using var client = factory.CreateAuthenticatedClient();

        using var response = await client.PatchAsJsonAsync(
            $"/api/entities/{EntityId}/progress",
            new EntityProgressUpdateRequest(ChapterId, "page", 3, 16, "paged", Completed: false));
        var repository = factory.Services.GetRequiredService<FakeEntityWriteRepository>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, repository.FullHydrationLookups);
        Assert.True(repository.ShallowLookups > 0);
    }

    [Fact]
    public async Task EntityProgressPatchIgnoresEarlierBookProgress() {
        using var factory = CreateProgressFactory();
        using var client = factory.CreateAuthenticatedClient();

        await client.PatchAsJsonAsync(
            $"/api/entities/{EntityId}/progress",
            new EntityProgressUpdateRequest(ChapterId, "page", 7, 24, "paged", Completed: null));
        using var response = await client.PatchAsJsonAsync(
            $"/api/entities/{EntityId}/progress",
            new EntityProgressUpdateRequest(ChapterId, "page", 2, 24, "paged", Completed: null));
        var repository = factory.Services.GetRequiredService<FakeEntityWriteRepository>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(7, repository.SavedEntity?.Progress?.Index);
    }

    private static EntityMetadataUpdateRequest Request(string title) =>
        new(
            ["title"],
            EmptyPatch() with { Title = title });

    private static EntityMetadataPatch EmptyPatch() => new(
        Title: null,
        Description: null,
        ExternalIds: new Dictionary<string, string>(),
        Urls: [],
        Tags: [],
        Studio: null,
        Credits: [],
        Dates: new Dictionary<string, string>(),
        Stats: new Dictionary<string, int>(),
        Positions: new Dictionary<string, int>(),
        Classification: null);

    private static WebApplicationFactory<Program> CreateFactory(EntityMetadataPatchResult result) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(new FakeEntityMetadataPatchService(result));
                    services.AddScoped<IEntityMetadataPatchService>(provider =>
                        provider.GetRequiredService<FakeEntityMetadataPatchService>());
                    services.AddScoped<IEntityReadService, FakeEntityReadService>();
                });
            })
            .WithTestAuth();

    private static WebApplicationFactory<Program> CreateProgressFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.ConfigureServices(services => {
                    services.AddSingleton(new FakeEntityWriteRepository());
                    services.AddScoped<IEntityWriteRepository>(provider =>
                        provider.GetRequiredService<FakeEntityWriteRepository>());
                });
            })
            .WithTestAuth();

    private sealed class FakeEntityMetadataPatchService(EntityMetadataPatchResult result) : IEntityMetadataPatchService {
        public Guid? EntityId { get; private set; }
        public string? ExpectedKind { get; private set; }

        public Task<EntityMetadataPatchResult> ApplyPatchAsync(
            Guid entityId,
            EntityMetadataUpdateRequest request,
            string? expectedKind,
            CancellationToken cancellationToken) {
            EntityId = entityId;
            ExpectedKind = expectedKind;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeEntityReadService : IEntityReadService {
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
            bool? orphaned = null) =>
            throw new NotSupportedException();

        public Task<EntityCard?> GetAsync(Guid id, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<EntityCard?>(Card(id, "video", "Updated Title"));

        public Task<EntityThumbnailBatchResponse> GetThumbnailsAsync(
            IReadOnlyList<Guid> ids,
            bool hideNsfw,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IEntityCard?> GetDetailAsync(Guid id, string kind, bool hideNsfw, CancellationToken cancellationToken) =>
            Task.FromResult<IEntityCard?>(Card(id, kind, kind == "video" ? "Video Title" : "Updated Title"));

        private static EntityCard Card(Guid id, string kind, string title) =>
            new() {
                Id = id,
                Kind = kind,
                Title = title,
                ParentEntityId = null,
                SortOrder = null,
                Capabilities = [],
                ChildrenByKind = [],
                Relationships = []
            };
    }

    private sealed class FakeEntityWriteRepository : IEntityWriteRepository {
        private readonly Book _entity = new(
            EntityId,
            "Book Title",
            BookType.Comic,
            coverPageId: null,
            capabilities: [new CapabilityProgress()]);
        private readonly BookVolume _volume = new(VolumeId, "Volume 1");
        private readonly BookChapter _chapter = new(ChapterId, "Chapter 1", coverPageId: null);

        public FakeEntityWriteRepository() {
            _entity.AddChild(_volume);
            _volume.AddChild(_chapter);
        }

        public Entity? SavedEntity { get; private set; }
        public int FullHydrationLookups { get; private set; }
        public int ShallowLookups { get; private set; }

        public Task<Entity?> FindAsync(Guid id, CancellationToken cancellationToken) {
            FullHydrationLookups++;
            return Task.FromResult(Find(id));
        }

        public Task<Entity?> FindShallowAsync(Guid id, CancellationToken cancellationToken) {
            ShallowLookups++;
            return Task.FromResult(Find(id));
        }

        private Entity? Find(Guid id) =>
            id == EntityId ? _entity : id == VolumeId ? _volume : id == ChapterId ? _chapter : null;

        public Task<Guid?> FindParentIdAsync(Guid id, CancellationToken cancellationToken) {
            Entity? entity = id == EntityId ? _entity : id == VolumeId ? _volume : id == ChapterId ? _chapter : null;
            return Task.FromResult(entity?.ParentEntityId);
        }

        public Task<BookProgressPosition?> ResolveBookProgressPositionAsync(
            Guid bookId,
            Guid currentEntityId,
            int index,
            int total,
            CancellationToken cancellationToken) =>
            Task.FromResult<BookProgressPosition?>(
                bookId == EntityId && currentEntityId == ChapterId
                    ? new BookProgressPosition(ChapterId, index, total)
                    : null);

        public Task SaveAsync(Entity entity, CancellationToken cancellationToken) {
            SavedEntity = entity;
            return Task.CompletedTask;
        }
    }
}
