using Prismedia.Application.Videos;
using Prismedia.Application.Subtitles;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.System;
using Prismedia.Api.Security;

namespace Prismedia.Api.Endpoints;

public static class VideoEndpoints {
    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder routes) {
        routes.MapVideoCatalogEndpoints();

        var group = routes.MapGroup("/api/videos")
            .WithTags("Videos");

        group.MapGet("/{id:guid}/subtitles/{trackId:guid}", StreamSubtitleAsync)
            .WithName("GetVideoSubtitle")
            .WithSummary("Gets one normalized WebVTT subtitle track.")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/subtitles/{trackId:guid}/source", StreamSubtitleSourceAsync)
            .WithName("GetVideoSubtitleSource")
            .WithSummary("Gets one preserved ASS/SSA subtitle source.")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/subtitles/search", SearchSubtitlesAsync)
            .WithName("SearchVideoSubtitles")
            .WithSummary("Searches configured subtitle providers for one video.")
            .RequireAdmin()
            .Produces<SearchVideoSubtitlesResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound)
            .Produces<ApiProblem>(StatusCodes.Status502BadGateway);

        group.MapPost("/{id:guid}/subtitles/download", AcquireSubtitleAsync)
            .WithName("AcquireVideoSubtitle")
            .WithSummary("Downloads and safely imports one provider subtitle candidate.")
            .RequireAdmin()
            .Produces<AcquireVideoSubtitleResponse>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound)
            .Produces<ApiProblem>(StatusCodes.Status410Gone)
            .Produces<ApiProblem>(StatusCodes.Status422UnprocessableEntity)
            .Produces<ApiProblem>(StatusCodes.Status502BadGateway);

        var providers = routes.MapGroup("/api/subtitle-providers")
            .WithTags("Subtitle Providers")
            .RequireAdmin();
        providers.MapGet("/opensubtitles", GetOpenSubtitlesConfigurationAsync)
            .WithName("GetOpenSubtitlesConfiguration")
            .Produces<OpenSubtitlesConfigurationResponse>();
        providers.MapPut("/opensubtitles", UpdateOpenSubtitlesConfigurationAsync)
            .WithName("UpdateOpenSubtitlesConfiguration")
            .Produces<OpenSubtitlesConfigurationResponse>();
        providers.MapPost("/opensubtitles/test", TestOpenSubtitlesAsync)
            .WithName("TestOpenSubtitlesConnection")
            .Produces<SubtitleProviderTestResponse>();

        return routes;
    }

    private static async Task<IResult> SearchSubtitlesAsync(
        Guid id,
        SearchVideoSubtitlesRequest request,
        ISubtitleAcquisitionService subtitles,
        CancellationToken cancellationToken) {
        try {
            var results = await subtitles.SearchAsync(
                id,
                new SubtitleSearchRequest(request.Languages),
                cancellationToken);
            return Results.Ok(new SearchVideoSubtitlesResponse(results.Select(ToCandidateResponse).ToArray()));
        } catch (KeyNotFoundException exception) {
            return Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, exception.Message));
        } catch (SubtitleProviderUnavailableException exception) {
            return Results.Json(
                new ApiProblem(ApiProblemCodes.SubtitleProviderUnavailable, exception.Message),
                statusCode: StatusCodes.Status502BadGateway);
        } catch (InvalidOperationException exception) {
            return Results.BadRequest(new ApiProblem(ApiProblemCodes.SubtitleProviderUnavailable, exception.Message));
        }
    }

    private static async Task<IResult> AcquireSubtitleAsync(
        Guid id,
        AcquireVideoSubtitleRequest request,
        ISubtitleAcquisitionService subtitles,
        CancellationToken cancellationToken) {
        try {
            var result = await subtitles.AcquireAsync(
                id,
                request.Provider,
                request.CandidateId,
                cancellationToken);
            return Results.Ok(new AcquireVideoSubtitleResponse(result.TrackId, result.AlreadyPresent));
        } catch (KeyNotFoundException exception) {
            return Results.NotFound(new ApiProblem(ApiProblemCodes.EntityNotFound, exception.Message));
        } catch (SubtitleCandidateUnavailableException exception) {
            return Results.Json(
                new ApiProblem(ApiProblemCodes.SubtitleCandidateUnavailable, exception.Message),
                statusCode: StatusCodes.Status410Gone);
        } catch (SubtitleImportException exception) {
            return Results.UnprocessableEntity(
                new ApiProblem(ApiProblemCodes.SubtitleImportFailed, exception.Message));
        } catch (SubtitleProviderUnavailableException exception) {
            return Results.Json(
                new ApiProblem(ApiProblemCodes.SubtitleProviderUnavailable, exception.Message),
                statusCode: StatusCodes.Status502BadGateway);
        } catch (ArgumentOutOfRangeException exception) {
            return Results.BadRequest(new ApiProblem(ApiProblemCodes.SubtitleProviderUnavailable, exception.Message));
        } catch (InvalidOperationException exception) {
            return Results.BadRequest(new ApiProblem(ApiProblemCodes.SubtitleProviderUnavailable, exception.Message));
        }
    }

    private static async Task<OpenSubtitlesConfigurationResponse> GetOpenSubtitlesConfigurationAsync(
        ISubtitleAcquisitionService subtitles,
        CancellationToken cancellationToken) =>
        ToConfigurationResponse(await subtitles.GetOpenSubtitlesConfigurationAsync(cancellationToken));

    private static async Task<OpenSubtitlesConfigurationResponse> UpdateOpenSubtitlesConfigurationAsync(
        UpdateOpenSubtitlesConfigurationRequest request,
        ISubtitleAcquisitionService subtitles,
        CancellationToken cancellationToken) =>
        ToConfigurationResponse(await subtitles.SaveOpenSubtitlesConfigurationAsync(
            new SaveOpenSubtitlesConfiguration(
                request.Enabled,
                request.ApiKey,
                request.Username,
                request.Password,
                request.IncludeAiTranslated,
                request.IncludeMachineTranslated),
            cancellationToken));

    private static async Task<SubtitleProviderTestResponse> TestOpenSubtitlesAsync(
        ISubtitleAcquisitionService subtitles,
        CancellationToken cancellationToken) {
        var result = await subtitles.TestOpenSubtitlesAsync(cancellationToken);
        return new SubtitleProviderTestResponse(result.Success, result.Message);
    }

    private static SubtitleCandidateResponse ToCandidateResponse(SubtitleSearchResult result) =>
        new(
            result.Provider,
            result.CandidateId,
            result.Language,
            result.ReleaseName,
            result.Format,
            result.HearingImpaired,
            result.Forced,
            result.AiTranslated,
            result.MachineTranslated,
            result.HashMatched,
            result.DownloadCount,
            result.Rating,
            result.MatchConfidence,
            result.QualityScore,
            result.AutomaticEligible,
            result.MatchReasons,
            result.PageUrl);

    private static OpenSubtitlesConfigurationResponse ToConfigurationResponse(OpenSubtitlesConfiguration configuration) =>
        new(
            configuration.Enabled,
            configuration.ApiKeyConfigured,
            configuration.UsernameConfigured,
            configuration.PasswordConfigured,
            configuration.IncludeAiTranslated,
            configuration.IncludeMachineTranslated);

    private static async Task<IResult> StreamSubtitleAsync(
        Guid id,
        Guid trackId,
        IVideoSubtitleAssetService subtitles,
        CancellationToken cancellationToken) {
        var subtitle = await subtitles.GetSubtitleAsync(id, trackId, cancellationToken);
        if (subtitle is null) {
            return Results.NotFound(new ApiProblem(
                ApiProblemCodes.VideoSubtitleNotFound,
                $"Subtitle track '{trackId}' for video '{id}' was not found."));
        }

        return Results.File(File.OpenRead(subtitle.Path), subtitle.ContentType);
    }

    private static async Task<IResult> StreamSubtitleSourceAsync(
        Guid id,
        Guid trackId,
        IVideoSubtitleAssetService subtitles,
        CancellationToken cancellationToken) {
        var subtitle = await subtitles.GetSubtitleSourceAsync(id, trackId, cancellationToken);
        if (subtitle is null) {
            return Results.NotFound(new ApiProblem(
                ApiProblemCodes.VideoSubtitleSourceNotFound,
                $"Subtitle source '{trackId}' for video '{id}' was not found."));
        }

        return Results.File(File.OpenRead(subtitle.Path), subtitle.ContentType);
    }
}
