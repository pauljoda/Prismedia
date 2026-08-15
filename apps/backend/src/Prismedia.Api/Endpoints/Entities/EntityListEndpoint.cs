using Microsoft.AspNetCore.Mvc;
using Prismedia.Application.Entities;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Api.Endpoints;

internal static class EntityListEndpoint {
    internal static RouteGroupBuilder MapEntityListEndpoint(this RouteGroupBuilder group) {
        group.MapGet("/", async (
            [AsParameters] EntityListParameters request,
            HttpContext httpContext,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            await ListAsync(request, httpContext, entities, cancellationToken))
            .WithName("ListEntities")
            .WithSummary("List Entities.")
            .Produces<EntityListResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapGet("/shelf", async (
            [AsParameters] EntityListParameters request,
            HttpContext httpContext,
            IEntityReadService entities,
            CancellationToken cancellationToken) =>
            await ShelfAsync(request, httpContext, entities, cancellationToken))
            .WithName("ListEntityShelf")
            .WithSummary("List a compact, count-free Entity shelf.")
            .Produces<EntityShelfResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        return group;
    }

    internal static async Task<IResult> ListAsync(
        EntityListParameters request,
        HttpContext httpContext,
        IEntityReadService entities,
        CancellationToken cancellationToken,
        string? requiredKind = null) {
        var resolvedKinds = requiredKind;
        if (resolvedKinds is null && !TryGetKinds(request.Kind, out resolvedKinds, out var error)) {
            return error;
        }

        if (!TryGetAcquisitionStatus(request.AcquisitionStatus, out var acquisitionStatus, out var statusError)) {
            return statusError;
        }
        if (!TryGetOptionalCode(request.Sort, "sort", out EntityListSort? sort, out var sortError)) {
            return sortError;
        }
        if (!TryGetOptionalCode(
                request.SortDirection,
                "sort direction",
                out EntitySortDirection? sortDirection,
                out var sortDirectionError)) {
            return sortDirectionError;
        }

        return Results.Ok(await entities.ListAsync(
            request.ToQuery(
                resolvedKinds,
                NsfwVisibility.ShouldHide(request.HideNsfw, httpContext),
                sort,
                sortDirection,
                acquisitionStatus),
            cancellationToken));
    }

    internal static async Task<IResult> ShelfAsync(
        EntityListParameters request,
        HttpContext httpContext,
        IEntityReadService entities,
        CancellationToken cancellationToken) {
        if (!TryGetKinds(request.Kind, out var resolvedKinds, out var error)) {
            return error;
        }
        if (!TryGetAcquisitionStatus(request.AcquisitionStatus, out var acquisitionStatus, out var statusError)) {
            return statusError;
        }
        if (!TryGetOptionalCode(request.Sort, "sort", out EntityListSort? sort, out var sortError)) {
            return sortError;
        }
        if (!TryGetOptionalCode(
                request.SortDirection,
                "sort direction",
                out EntitySortDirection? sortDirection,
                out var sortDirectionError)) {
            return sortDirectionError;
        }

        return Results.Ok(await entities.ListShelfAsync(
            request.ToQuery(
                resolvedKinds,
                NsfwVisibility.ShouldHide(request.HideNsfw, httpContext),
                sort,
                sortDirection,
                acquisitionStatus),
            cancellationToken));
    }

    private static bool TryGetKinds(string? value, out string? kinds, out IResult error) {
        kinds = null;
        error = Results.Empty;
        if (string.IsNullOrWhiteSpace(value)) {
            return true;
        }

        var resolvedKinds = new List<string>();
        foreach (var token in value.Split(',', StringSplitOptions.TrimEntries)) {
            if (!string.IsNullOrEmpty(token) && EntityKindRegistry.TryGet(token, out var resolved)) {
                var code = resolved.ToCode();
                if (!resolvedKinds.Contains(code, StringComparer.OrdinalIgnoreCase)) {
                    resolvedKinds.Add(code);
                }
                continue;
            }

            error = Results.BadRequest(new ApiProblem(
                ApiProblemCodes.InvalidEntityKind,
                $"Entity kind '{token}' is not recognized."));
            return false;
        }

        kinds = string.Join(',', resolvedKinds);
        return true;
    }

    private static bool TryGetAcquisitionStatus(
        string? value,
        out AcquisitionStatus? status,
        out IResult error) {
        status = null;
        error = Results.Empty;
        if (string.IsNullOrWhiteSpace(value)) {
            return true;
        }

        if (value.TryDecodeAs<AcquisitionStatus>(out var decoded)) {
            status = decoded;
            return true;
        }

        error = Results.BadRequest(new ApiProblem(
            ApiProblemCodes.RequestInvalid,
            $"Acquisition status '{value}' is not recognized."));
        return false;
    }

    private static bool TryGetOptionalCode<TValue>(
        string? value,
        string label,
        out TValue? decoded,
        out IResult error)
        where TValue : struct, Enum {
        decoded = null;
        error = Results.Empty;
        if (string.IsNullOrWhiteSpace(value)) {
            return true;
        }
        if (value.Trim().TryDecodeAs<TValue>(out var result)) {
            decoded = result;
            return true;
        }

        error = Results.BadRequest(new ApiProblem(
            ApiProblemCodes.RequestInvalid,
            $"Entity {label} '{value}' is not recognized."));
        return false;
    }
}

internal sealed record EntityListParameters {
    [FromQuery(Name = "kind")]
    public string? Kind { get; init; }

    [FromQuery(Name = "query")]
    public string? Query { get; init; }

    [FromQuery(Name = "cursor")]
    public string? Cursor { get; init; }

    [FromQuery(Name = "hideNsfw")]
    public bool? HideNsfw { get; init; }

    [FromQuery(Name = "limit")]
    public int? Limit { get; init; }

    [FromQuery(Name = "referencedBy")]
    public Guid? ReferencedBy { get; init; }

    [FromQuery(Name = "relationshipCode")]
    public string? RelationshipCode { get; init; }

    [FromQuery(Name = "sort")]
    public string? Sort { get; init; }

    [FromQuery(Name = "sortDirection")]
    public string? SortDirection { get; init; }

    [FromQuery(Name = "seed")]
    public int? Seed { get; init; }

    [FromQuery(Name = "favorite")]
    public bool? Favorite { get; init; }

    [FromQuery(Name = "organized")]
    public bool? Organized { get; init; }

    [FromQuery(Name = "ratingMin")]
    public int? RatingMin { get; init; }

    [FromQuery(Name = "ratingMax")]
    public int? RatingMax { get; init; }

    [FromQuery(Name = "unrated")]
    public bool? Unrated { get; init; }

    [FromQuery(Name = "status")]
    public string? Status { get; init; }

    [FromQuery(Name = "bookType")]
    public string? BookType { get; init; }

    [FromQuery(Name = "bookFormat")]
    public string? BookFormat { get; init; }

    [FromQuery(Name = "nsfw")]
    public bool? Nsfw { get; init; }

    [FromQuery(Name = "hasFile")]
    public bool? HasFile { get; init; }

    [FromQuery(Name = "engaged")]
    public bool? Engaged { get; init; }

    [FromQuery(Name = "orphaned")]
    public bool? Orphaned { get; init; }

    [FromQuery(Name = "wanted")]
    public bool? Wanted { get; init; }

    [FromQuery(Name = "acquisitionStatus")]
    public string? AcquisitionStatus { get; init; }

    public EntityListQuery ToQuery(
        string? kind,
        bool? hideNsfw,
        EntityListSort? sort,
        EntitySortDirection? sortDirection,
        AcquisitionStatus? acquisitionStatus = null) => new() {
        Kind = kind,
        Query = Query,
        Cursor = Cursor,
        HideNsfw = hideNsfw,
        Limit = Limit,
        ReferencedBy = ReferencedBy,
        RelationshipCode = RelationshipCode,
        Sort = sort,
        SortDirection = sortDirection,
        Seed = Seed,
        Favorite = Favorite,
        Organized = Organized,
        RatingMin = RatingMin,
        RatingMax = RatingMax,
        Unrated = Unrated,
        Status = Status,
        BookType = BookType,
        BookFormat = BookFormat,
        Nsfw = Nsfw,
        HasFile = HasFile,
        Engaged = Engaged,
        Orphaned = Orphaned,
        Wanted = Wanted,
        AcquisitionStatus = acquisitionStatus,
    };
}
