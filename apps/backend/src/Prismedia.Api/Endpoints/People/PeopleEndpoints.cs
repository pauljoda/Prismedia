using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Taxonomy;

namespace Prismedia.Api.Endpoints;

public static class PeopleEndpoints {
    public static RouteGroupBuilder MapPeopleEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/people",
            "person",
            "Taxonomy",
            "ListPeople",
            "GetPerson",
            typeof(EntityListResponse),
            typeof(PersonDetail));
}
