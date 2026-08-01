using Prismedia.Contracts.Entities;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

public static class PeopleEndpoints {
    public static RouteGroupBuilder MapPeopleEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapEntityKindRoutes(
            "/api/people",
            EntityKind.Person.ToCode(),
            "Taxonomy",
            "ListPeople",
            "GetPerson");
}
