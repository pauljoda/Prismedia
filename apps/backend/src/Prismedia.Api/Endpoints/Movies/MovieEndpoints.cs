using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

/// <summary>Maps movie catalog endpoints for first-class movie entities.</summary>
public static class MovieEndpoints {
    /// <summary>Registers list and detail routes for movies.</summary>
    public static RouteGroupBuilder MapMovieEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/movies",
            EntityKindRegistry.Movie.Code,
            "Movies",
            "ListMovies",
            "GetMovie");
}
