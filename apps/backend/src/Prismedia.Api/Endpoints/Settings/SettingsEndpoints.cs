using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prismedia.Application.Backups;
using Prismedia.Application.Settings;
using Prismedia.Application.Videos;
using Prismedia.Contracts.Settings;
using Prismedia.Contracts.System;

using Prismedia.Api.Security;

namespace Prismedia.Api.Endpoints;

public static class SettingsEndpoints {
    private static readonly TimeSpan RestoreTransitionDelay = TimeSpan.FromSeconds(1.5);

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
            .RequireAdmin()
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
            .RequireAdmin()
            .WithName("ClearTranscodeCache")
            .WithSummary("Clears the on-disk transcode cache.")
            .Produces<TranscodeCacheStatusResponse>();

        group.MapGet("/database-backups", (
            IDatabaseBackupService backups,
            CancellationToken cancellationToken) =>
            backups.ListAsync(cancellationToken))
            .RequireAdmin()
            .WithName("ListDatabaseBackups")
            .WithSummary("Lists database backups and automatic backup retention metadata.")
            .Produces<DatabaseBackupListResponse>();

        group.MapPost("/database-backups/now", async (
            IDatabaseBackupService backups,
            CancellationToken cancellationToken) => {
            try {
                return Results.Ok(await backups.CreateManualBackupAsync(cancellationToken));
            } catch (DatabaseBackupException ex) {
                return BackupFailure(ex);
            }
        })
            .RequireAdmin()
            .WithName("CreateDatabaseBackupNow")
            .WithSummary("Creates a permanent manual database backup.")
            .Produces<DatabaseBackupDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/database-backups/restore", async (
            DatabaseRestoreRequest request,
            IDatabaseBackupService backups,
            IHostApplicationLifetime lifetime,
            IHostEnvironment environment,
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) => {
            try {
                var response = await backups.ScheduleRestoreAsync(
                    request.BackupId,
                    request.ConfirmationText,
                    cancellationToken);

                if (ShouldRestoreInProcess(environment, configuration)) {
                    _ = Task.Run(
                        () => RunRestoreInProcessAsync(scopeFactory, loggerFactory, RestoreTransitionDelay),
                        CancellationToken.None);
                    return Results.Ok(response with { RestartScheduled = false });
                }

                _ = Task.Run(async () => {
                    await Task.Delay(RestoreTransitionDelay, CancellationToken.None);
                    lifetime.StopApplication();
                }, CancellationToken.None);
                return Results.Ok(response);
            } catch (DatabaseBackupException ex) {
                return BackupFailure(ex);
            }
        })
            .RequireAdmin()
            .WithName("RestoreDatabaseBackup")
            .WithSummary("Schedules a destructive database restore from a selected backup.")
            .Produces<DatabaseRestoreScheduledResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", (
            SettingsService settings,
            CancellationToken cancellationToken) =>
            settings.GetCatalogAsync(cancellationToken))
            .RequireAdmin()
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
            .RequireAdmin()
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
            .RequireAdmin()
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
            .RequireAdmin()
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
            .RequireAdmin()
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
            .RequireAdmin()
            .WithName("ResetSetting")
            .WithSummary("Removes one app-global setting override.")
            .Produces<SettingDescriptor>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static IResult SettingNotFound(SettingNotFoundException ex) =>
        Results.NotFound(new ApiProblem(ApiProblemCodes.SettingNotFound, ex.Message));

    private static IResult InvalidSetting(SettingValidationException ex) =>
        Results.BadRequest(new ApiProblem(ApiProblemCodes.SettingInvalid, ex.Message));

    private static IResult BackupFailure(DatabaseBackupException ex) =>
        ex.ProblemCode == ApiProblemCodes.DatabaseBackupNotFound
            ? Results.NotFound(new ApiProblem(ex.ProblemCode, ex.Message))
            : Results.BadRequest(new ApiProblem(ex.ProblemCode, ex.Message));

    private static bool ShouldRestoreInProcess(IHostEnvironment environment, IConfiguration configuration) {
        var configured = configuration["PRISMEDIA_BACKUP_RESTORE_IN_PROCESS"] ??
            configuration["Prismedia:Backups:RestoreInProcess"];
        if (bool.TryParse(configured, out var restoreInProcess)) {
            return restoreInProcess;
        }

        return environment.IsDevelopment();
    }

    private static async Task RunRestoreInProcessAsync(
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory,
        TimeSpan delay) {
        var logger = loggerFactory.CreateLogger("Prismedia.DatabaseRestore");
        try {
            await Task.Delay(delay, CancellationToken.None);
            await using var scope = scopeFactory.CreateAsyncScope();
            var backups = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();
            await backups.RunPendingRestoreAsync(CancellationToken.None);
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to apply pending database restore in the running API process.");
        }
    }
}
