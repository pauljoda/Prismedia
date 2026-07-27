using Microsoft.AspNetCore.Mvc;
using Prismedia.Api.Security;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

/// <summary>Admin release calendar over actively monitored requests.</summary>
public static class ReleaseCalendarEndpoints {
    private const int MaximumRangeDays = 370;

    public static RouteGroupBuilder MapReleaseCalendarEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/calendar")
            .RequireAdmin()
            .WithTags("Calendar");

        group.MapGet("/releases", async (
            [FromQuery] DateOnly start,
            [FromQuery] DateOnly end,
            HttpContext httpContext,
            IReleaseCalendarService calendar,
            CancellationToken cancellationToken) => {
                if (end < start || end.DayNumber - start.DayNumber > MaximumRangeDays) {
                    return Results.BadRequest(new ApiProblem(
                        ApiProblemCodes.CalendarRangeInvalid,
                        $"Calendar ranges must be ordered and no longer than {MaximumRangeDays} days."));
                }

                return Results.Ok(await calendar.ListAsync(
                    start,
                    end,
                    NsfwVisibility.ShouldHide(null, httpContext),
                    cancellationToken));
            })
            .WithName("ListReleaseCalendar")
            .WithSummary("Lists typed release milestones for actively monitored requests.")
            .Produces<IReadOnlyList<ReleaseCalendarEvent>>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        return group;
    }
}
