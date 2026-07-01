using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Endpoints;

public static class AcquisitionEndpoints {
    public static RouteGroupBuilder MapAcquisitionEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/acquisitions")
            .WithTags("Acquisitions");

        group.MapGet("/indexers", (
            IndexerConfigCommandService indexers,
            CancellationToken cancellationToken) =>
            indexers.ListAsync(cancellationToken))
            .WithName("ListIndexers")
            .WithSummary("Lists configured indexers with API keys redacted.")
            .Produces<IReadOnlyList<IndexerConfigSummary>>();

        group.MapPost("/indexers", async (
            IndexerConfigSaveRequest request,
            IndexerConfigCommandService indexers,
            CancellationToken cancellationToken) =>
            await SaveIndexerAsync(request, indexers, cancellationToken))
            .WithName("SaveIndexer")
            .WithSummary("Creates or updates an indexer configuration.")
            .Produces<IndexerConfigSummary>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPut("/indexers/{id:guid}", async (
            Guid id,
            IndexerConfigSaveRequest request,
            IndexerConfigCommandService indexers,
            CancellationToken cancellationToken) =>
            await SaveIndexerAsync(request with { Id = id }, indexers, cancellationToken))
            .WithName("UpdateIndexer")
            .WithSummary("Updates an existing indexer configuration.")
            .Produces<IndexerConfigSummary>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapDelete("/indexers/{id:guid}", async (
            Guid id,
            IndexerConfigCommandService indexers,
            CancellationToken cancellationToken) =>
            await indexers.DeleteAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Indexer was not found.")))
            .WithName("DeleteIndexer")
            .WithSummary("Deletes a configured indexer.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/indexers/test", async (
            IndexerTestRequest request,
            IndexerConfigCommandService indexers,
            CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await indexers.TestAsync(request, cancellationToken));
                } catch (AcquisitionConfigurationException ex) {
                    return Results.BadRequest(new ApiProblem(ex.Code, ex.Message));
                }
            })
            .WithName("TestIndexer")
            .WithSummary("Tests connectivity for an indexer configuration.")
            .Produces<IndexerTestResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPost("/search", async (
            AcquisitionCreateRequest request,
            AcquisitionService acquisitions,
            CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await acquisitions.CreateAndSearchAsync(request, cancellationToken));
                } catch (AcquisitionConfigurationException ex) {
                    return Results.BadRequest(new ApiProblem(ex.Code, ex.Message));
                }
            })
            .WithName("CreateAcquisition")
            .WithSummary("Creates an acquisition and starts a background indexer search; poll the acquisition for scored candidates.")
            .Produces<AcquisitionSummary>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapGet("/", (
            AcquisitionService acquisitions,
            CancellationToken cancellationToken) =>
            acquisitions.ListAsync(cancellationToken))
            .WithName("ListAcquisitions")
            .WithSummary("Lists acquisitions with their current status.")
            .Produces<IReadOnlyList<AcquisitionSummary>>();

        group.MapGet("/{id:guid}", async (
            Guid id,
            AcquisitionService acquisitions,
            CancellationToken cancellationToken) => {
                var detail = await acquisitions.GetAsync(id, cancellationToken);
                return detail is null
                    ? Results.NotFound(new ApiProblem(ApiProblemCodes.AcquisitionNotFound, "Acquisition was not found."))
                    : Results.Ok(detail);
            })
            .WithName("GetAcquisition")
            .WithSummary("Gets an acquisition with its scored release candidates for review.")
            .Produces<AcquisitionDetail>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapGet("/for-entity/{entityId:guid}", async (
            Guid entityId,
            AcquisitionService acquisitions,
            CancellationToken cancellationToken) => {
                var detail = await acquisitions.GetForEntityAsync(entityId, cancellationToken);
                return detail is null
                    ? Results.NotFound(new ApiProblem(ApiProblemCodes.AcquisitionNotFound, "The entity has no acquisition."))
                    : Results.Ok(detail);
            })
            .WithName("GetAcquisitionForEntity")
            .WithSummary("Gets the latest acquisition backing a library entity, so entity detail pages can surface its wanted/tracking state inline.")
            .Produces<AcquisitionDetail>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/queue", async (
            Guid id,
            AcquisitionQueueRequest request,
            AcquisitionQueueService queue,
            CancellationToken cancellationToken) => {
                try {
                    var detail = await queue.QueueAsync(id, request.CandidateId, cancellationToken);
                    return detail is null
                        ? Results.NotFound(new ApiProblem(ApiProblemCodes.AcquisitionNotFound, "Acquisition was not found."))
                        : Results.Ok(detail);
                } catch (AcquisitionConfigurationException ex) {
                    return Results.BadRequest(new ApiProblem(ex.Code, ex.Message));
                }
            })
            .WithName("QueueAcquisition")
            .WithSummary("Sends a chosen release to the download client and begins tracking the transfer.")
            .Produces<AcquisitionDetail>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/candidates/{candidateId:guid}/blocklist", async (
            Guid id,
            Guid candidateId,
            AcquisitionService acquisitions,
            CancellationToken cancellationToken) => {
                var detail = await acquisitions.BlocklistCandidateAsync(id, candidateId, cancellationToken);
                return detail is null
                    ? Results.NotFound(new ApiProblem(ApiProblemCodes.AcquisitionReleaseNotFound, "The release was not found for this acquisition."))
                    : Results.Ok(detail);
            })
            .WithName("BlocklistAcquisitionCandidate")
            .WithSummary("Blocklists a release from an acquisition's candidates so it is never grabbed again.")
            .Produces<AcquisitionDetail>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/search", async (
            Guid id,
            AcquisitionService acquisitions,
            CancellationToken cancellationToken) => {
                var detail = await acquisitions.ReSearchAsync(id, cancellationToken);
                return detail is null
                    ? Results.NotFound(new ApiProblem(ApiProblemCodes.AcquisitionNotFound, "Acquisition was not found."))
                    : Results.Ok(detail);
            })
            .WithName("ReSearchAcquisition")
            .WithSummary("Re-runs the release search for an existing acquisition on demand.")
            .Produces<AcquisitionDetail>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/cancel", async (
            Guid id,
            AcquisitionService acquisitions,
            CancellationToken cancellationToken) => {
                var detail = await acquisitions.CancelAsync(id, cancellationToken);
                return detail is null
                    ? Results.NotFound(new ApiProblem(ApiProblemCodes.AcquisitionNotFound, "Acquisition was not found."))
                    : Results.Ok(detail);
            })
            .WithName("CancelAcquisition")
            .WithSummary("Cancels an acquisition, removing the torrent from the download client.")
            .Produces<AcquisitionDetail>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            AcquisitionService acquisitions,
            CancellationToken cancellationToken) =>
            await acquisitions.DeleteAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem(ApiProblemCodes.AcquisitionNotFound, "Acquisition was not found.")))
            .WithName("DeleteAcquisition")
            .WithSummary("Removes an acquisition and its torrent (and downloaded data) from the download client.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/transfer", async (
            Guid id,
            AcquisitionService acquisitions,
            CancellationToken cancellationToken) => {
                var transfer = await acquisitions.GetTransferAsync(id, cancellationToken);
                return transfer is null ? Results.NoContent() : Results.Ok(transfer);
            })
            .WithName("GetAcquisitionTransfer")
            .WithSummary("Live transfer telemetry (progress, speed, ETA, peers, per-piece state) for an in-flight acquisition.")
            .Produces<AcquisitionTransferView>()
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/{id:guid}/files", (
            Guid id,
            AcquisitionService acquisitions,
            CancellationToken cancellationToken) =>
            acquisitions.GetFilesAsync(id, cancellationToken))
            .WithName("GetAcquisitionFiles")
            .WithSummary("The acquisition's files: imported library files once imported, otherwise the in-progress download files.")
            .Produces<AcquisitionFilesView>();

        group.MapPost("/{id:guid}/upload-torrent", async (
            Guid id,
            IFormFile file,
            AcquisitionQueueService queue,
            CancellationToken cancellationToken) => {
                if (file.Length == 0) {
                    return Results.BadRequest(new ApiProblem(ApiProblemCodes.AcquisitionInvalid, "The uploaded torrent file is empty."));
                }

                using var stream = new MemoryStream();
                await file.CopyToAsync(stream, cancellationToken);
                try {
                    var detail = await queue.QueueManualTorrentAsync(id, file.FileName, stream.ToArray(), cancellationToken);
                    return detail is null
                        ? Results.NotFound(new ApiProblem(ApiProblemCodes.AcquisitionNotFound, "Acquisition was not found."))
                        : Results.Ok(detail);
                } catch (AcquisitionConfigurationException ex) {
                    return Results.BadRequest(new ApiProblem(ex.Code, ex.Message));
                }
            })
            .WithName("UploadAcquisitionTorrent")
            .WithSummary("Queues an acquisition from a user-supplied .torrent file (manual fallback for linkless releases).")
            .DisableAntiforgery()
            .Produces<AcquisitionDetail>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapGet("/download-clients", (
            DownloadClientCommandService downloadClients,
            CancellationToken cancellationToken) =>
            downloadClients.ListAsync(cancellationToken))
            .WithName("ListDownloadClients")
            .WithSummary("Lists configured download clients with passwords redacted.")
            .Produces<IReadOnlyList<DownloadClientSummary>>();

        group.MapPost("/download-clients", async (
            DownloadClientSaveRequest request,
            DownloadClientCommandService downloadClients,
            CancellationToken cancellationToken) =>
            await SaveDownloadClientAsync(request, downloadClients, cancellationToken))
            .WithName("SaveDownloadClient")
            .WithSummary("Creates or updates a download client configuration.")
            .Produces<DownloadClientSummary>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPut("/download-clients/{id:guid}", async (
            Guid id,
            DownloadClientSaveRequest request,
            DownloadClientCommandService downloadClients,
            CancellationToken cancellationToken) =>
            await SaveDownloadClientAsync(request with { Id = id }, downloadClients, cancellationToken))
            .WithName("UpdateDownloadClient")
            .WithSummary("Updates an existing download client configuration.")
            .Produces<DownloadClientSummary>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapDelete("/download-clients/{id:guid}", async (
            Guid id,
            DownloadClientCommandService downloadClients,
            CancellationToken cancellationToken) =>
            await downloadClients.DeleteAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Download client was not found.")))
            .WithName("DeleteDownloadClient")
            .WithSummary("Deletes a configured download client.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/download-clients/test", async (
            DownloadClientTestRequest request,
            DownloadClientCommandService downloadClients,
            CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await downloadClients.TestAsync(request, cancellationToken));
                } catch (AcquisitionConfigurationException ex) {
                    return Results.BadRequest(new ApiProblem(ex.Code, ex.Message));
                }
            })
            .WithName("TestDownloadClient")
            .WithSummary("Tests connectivity for a download client configuration.")
            .Produces<DownloadClientTestResponse>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapGet("/profiles", (
            BookAcquisitionProfileCommandService profiles,
            CancellationToken cancellationToken) =>
            profiles.ListAsync(cancellationToken))
            .WithName("ListAcquisitionProfiles")
            .WithSummary("Lists book acquisition profiles (matching rules and import target).")
            .Produces<IReadOnlyList<BookAcquisitionProfileView>>();

        group.MapPost("/profiles", async (
            BookAcquisitionProfileSaveRequest request,
            BookAcquisitionProfileCommandService profiles,
            CancellationToken cancellationToken) =>
            await SaveProfileAsync(request, profiles, cancellationToken))
            .WithName("SaveAcquisitionProfile")
            .WithSummary("Creates or updates a book acquisition profile.")
            .Produces<BookAcquisitionProfileView>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapPut("/profiles/{id:guid}", async (
            Guid id,
            BookAcquisitionProfileSaveRequest request,
            BookAcquisitionProfileCommandService profiles,
            CancellationToken cancellationToken) =>
            await SaveProfileAsync(request with { Id = id }, profiles, cancellationToken))
            .WithName("UpdateAcquisitionProfile")
            .WithSummary("Updates an existing book acquisition profile.")
            .Produces<BookAcquisitionProfileView>()
            .Produces<ApiProblem>(StatusCodes.Status400BadRequest);

        group.MapDelete("/profiles/{id:guid}", async (
            Guid id,
            BookAcquisitionProfileCommandService profiles,
            CancellationToken cancellationToken) =>
            await profiles.DeleteAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Acquisition profile was not found.")))
            .WithName("DeleteAcquisitionProfile")
            .WithSummary("Deletes a book acquisition profile.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapGet("/blocklist", (
            IAcquisitionBlocklistStore blocklist,
            CancellationToken cancellationToken) =>
            blocklist.ListAsync(cancellationToken))
            .WithName("ListAcquisitionBlocklist")
            .WithSummary("Lists blocklisted releases that failed-download recovery refuses for future acquisition.")
            .Produces<IReadOnlyList<AcquisitionBlocklistEntry>>();

        group.MapDelete("/blocklist/{id:guid}", async (
            Guid id,
            IAcquisitionBlocklistStore blocklist,
            CancellationToken cancellationToken) =>
            await blocklist.DeleteAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Blocklist entry was not found.")))
            .WithName("DeleteAcquisitionBlocklistEntry")
            .WithSummary("Removes a release from the blocklist so it can be acquired again.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    /// <summary>Maps the monitor endpoints (start/stop/pause/resume + list) for keeping a wanted item's search alive.</summary>
    public static RouteGroupBuilder MapMonitorEndpoints(this IEndpointRouteBuilder routes) {
        var group = routes.MapGroup("/api/monitors")
            .WithTags("Monitors");

        group.MapGet("/", (
            MonitorService monitors,
            CancellationToken cancellationToken) =>
            monitors.ListAsync(cancellationToken))
            .WithName("ListMonitors")
            .WithSummary("Lists monitored items with the status of each linked acquisition.")
            .Produces<IReadOnlyList<MonitorView>>();

        group.MapPost("/", async (
            MonitorCreateRequest request,
            MonitorService monitors,
            CancellationToken cancellationToken) => {
                var monitor = await monitors.StartAsync(request.AcquisitionId, cancellationToken);
                return monitor is null
                    ? Results.NotFound(new ApiProblem(ApiProblemCodes.AcquisitionNotFound, "Acquisition was not found."))
                    : Results.Ok(monitor);
            })
            .WithName("StartMonitor")
            .WithSummary("Starts monitoring an acquisition so its release search is re-run until the item is acquired.")
            .Produces<MonitorView>()
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            MonitorService monitors,
            CancellationToken cancellationToken) =>
            await monitors.StopAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Monitor was not found.")))
            .WithName("StopMonitor")
            .WithSummary("Stops monitoring (the acquisition is left untouched).")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/pause", async (
            Guid id,
            MonitorService monitors,
            CancellationToken cancellationToken) =>
            await monitors.PauseAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Monitor was not found.")))
            .WithName("PauseMonitor")
            .WithSummary("Pauses a monitor so it is not re-searched until resumed.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/resume", async (
            Guid id,
            MonitorService monitors,
            CancellationToken cancellationToken) =>
            await monitors.ResumeAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound(new ApiProblem(ApiProblemCodes.NotFound, "Monitor was not found.")))
            .WithName("ResumeMonitor")
            .WithSummary("Resumes a paused monitor.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> SaveIndexerAsync(
        IndexerConfigSaveRequest request,
        IndexerConfigCommandService indexers,
        CancellationToken cancellationToken) {
        try {
            return Results.Ok(await indexers.SaveAsync(request, cancellationToken));
        } catch (AcquisitionConfigurationException ex) {
            return Results.BadRequest(new ApiProblem(ex.Code, ex.Message));
        }
    }

    private static async Task<IResult> SaveDownloadClientAsync(
        DownloadClientSaveRequest request,
        DownloadClientCommandService downloadClients,
        CancellationToken cancellationToken) {
        try {
            return Results.Ok(await downloadClients.SaveAsync(request, cancellationToken));
        } catch (AcquisitionConfigurationException ex) {
            return Results.BadRequest(new ApiProblem(ex.Code, ex.Message));
        }
    }

    private static async Task<IResult> SaveProfileAsync(
        BookAcquisitionProfileSaveRequest request,
        BookAcquisitionProfileCommandService profiles,
        CancellationToken cancellationToken) {
        try {
            return Results.Ok(await profiles.SaveAsync(request, cancellationToken));
        } catch (AcquisitionConfigurationException ex) {
            return Results.BadRequest(new ApiProblem(ex.Code, ex.Message));
        }
    }
}
