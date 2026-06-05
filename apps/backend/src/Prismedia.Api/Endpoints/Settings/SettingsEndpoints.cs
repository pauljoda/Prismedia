using Prismedia.Application.Settings;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Settings;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

public static class SettingsEndpoints {
    public static RouteGroupBuilder MapSettingsEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/settings")
            .WithTags("Settings");

        group.MapGet("/transcode-cache", async (
            ITranscodeCacheService cache,
            SettingsService settings,
            CancellationToken cancellationToken) => {
            var hls = await settings.GetHlsSettingsAsync(cancellationToken);
            return Results.Ok(new TranscodeCacheStatusResponse(
                cache.ComputeSizeBytes(),
                ITranscodeCacheService.GigabytesToBytes(hls.MaxCacheSizeGb)));
        })
            .WithName("GetTranscodeCacheStatus")
            .WithSummary("Gets the current transcode cache size and configured limit.")
            .Produces<TranscodeCacheStatusResponse>();

        group.MapPost("/transcode-cache/clear", async (
            ITranscodeCacheService cache,
            SettingsService settings,
            CancellationToken cancellationToken) => {
            cache.Clear();
            var hls = await settings.GetHlsSettingsAsync(cancellationToken);
            return Results.Ok(new TranscodeCacheStatusResponse(
                cache.ComputeSizeBytes(),
                ITranscodeCacheService.GigabytesToBytes(hls.MaxCacheSizeGb)));
        })
            .WithName("ClearTranscodeCache")
            .WithSummary("Clears the on-disk transcode cache.")
            .Produces<TranscodeCacheStatusResponse>();

        group.MapGet("/", (
            SettingsService settings,
            CancellationToken cancellationToken) =>
            settings.GetCatalogAsync(cancellationToken))
            .WithName("GetSettings")
            .WithSummary("Gets the app-global settings catalog.");

        group.MapGet("/values", (
            string[]? keys,
            SettingsService settings,
            CancellationToken cancellationToken) =>
            settings.GetValuesAsync(keys ?? [], cancellationToken))
            .WithName("GetSettingValues")
            .WithSummary("Gets lightweight app-global setting values.");

        group.MapGet("/library", (
            SettingsService settings,
            CancellationToken cancellationToken) =>
            settings.GetLibraryConfigAsync(cancellationToken))
            .WithName("GetLibraryConfig")
            .WithSummary("Gets the settings catalog and watched roots for the settings page.");

        group.MapGet("/{key}", async (
            string key,
            SettingsService settings,
            CancellationToken cancellationToken) => {
            try {
                return Results.Ok(await settings.GetSettingAsync(key, cancellationToken));
            } catch (SettingNotFoundException ex) {
                return SettingNotFound(ex);
            }
        })
            .WithName("GetSetting")
            .WithSummary("Gets one app-global setting descriptor.")
            .Produces<SettingDescriptor>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/", async (
            SettingsBatchUpdateRequest request,
            SettingsService settings,
            CancellationToken cancellationToken) => {
            try {
                return Results.Ok(await settings.UpdateSettingsAsync(request.Values, cancellationToken));
            } catch (SettingNotFoundException ex) {
                return SettingNotFound(ex);
            } catch (SettingValidationException ex) {
                return InvalidSetting(ex);
            }
        })
            .WithName("UpdateSettings")
            .WithSummary("Updates multiple app-global settings.")
            .Produces<SettingsCatalogResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{key}", async (
            string key,
            SettingUpdateRequest request,
            SettingsService settings,
            CancellationToken cancellationToken) => {
            try {
                return Results.Ok(await settings.UpdateSettingAsync(key, request.Value, cancellationToken));
            } catch (SettingNotFoundException ex) {
                return SettingNotFound(ex);
            } catch (SettingValidationException ex) {
                return InvalidSetting(ex);
            }
        })
            .WithName("UpdateSetting")
            .WithSummary("Updates one app-global setting.")
            .Produces<SettingDescriptor>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{key}", async (
            string key,
            SettingsService settings,
            CancellationToken cancellationToken) => {
            try {
                return Results.Ok(await settings.ResetSettingAsync(key, cancellationToken));
            } catch (SettingNotFoundException ex) {
                return SettingNotFound(ex);
            }
        })
            .WithName("ResetSetting")
            .WithSummary("Removes one app-global setting override.")
            .Produces<SettingDescriptor>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static IResult SettingNotFound(SettingNotFoundException ex) =>
        Results.NotFound(new ApiProblem("setting_not_found", ex.Message));

    private static IResult InvalidSetting(SettingValidationException ex) =>
        Results.BadRequest(new ApiProblem("setting_invalid", ex.Message));
}
