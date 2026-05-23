using Prismedia.Application.Organization;
using Prismedia.Contracts.Organize;

namespace Prismedia.Api.Endpoints;

public static class OrganizeEndpoints {
    public static RouteGroupBuilder MapOrganizeEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/organize")
            .WithTags("Organize");

        group.MapGet("/plan", (
            Guid? entityId,
            Guid? rootId,
            OrganizeService organize,
            CancellationToken cancellationToken) =>
            organize.PlanAsync(new OrganizePlanRequest(entityId, rootId), cancellationToken))
            .WithName("GetOrganizePlan")
            .WithSummary("Computes a dry-run entity organization plan from generic storage metadata.")
            .Produces<OrganizePlanResponse>();

        group.MapPost("/apply", (
            OrganizePlanRequest request,
            OrganizeService organize,
            CancellationToken cancellationToken) =>
            organize.ApplyAsync(request, cancellationToken))
            .WithName("ApplyOrganizePlan")
            .WithSummary("Applies an entity organization plan by moving source files or folders.")
            .Produces<OrganizeApplyResponse>();

        return group;
    }
}
