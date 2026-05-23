using Prismedia.Application.Entities;
using Prismedia.Contracts.System;
using System.IO.Compression;

namespace Prismedia.Api.Endpoints;

internal static class EntityFileEndpoint {
    internal static RouteGroupBuilder MapEntityFileEndpoint(this RouteGroupBuilder group) {
        group.MapGet("/{id:guid}/files/{role}", StreamEntityFileAsync)
            .WithName("GetEntityFile")
            .WithSummary("Streams an entity-attached file by semantic role.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status206PartialContent)
            .Produces<ApiProblem>(StatusCodes.Status404NotFound);

        group.MapMethods("/{id:guid}/files/{role}", [HttpMethods.Head], StreamEntityFileAsync)
            .WithName("HeadEntityFile")
            .WithSummary("Probes an entity-attached file by semantic role.");

        return group;
    }

    private static async Task<IResult> StreamEntityFileAsync(
        Guid id,
        string role,
        IEntityFileContentService files,
        CancellationToken cancellationToken) {
        var content = await files.GetContentAsync(id, role, cancellationToken);
        if (content is null) {
            return Results.NotFound(new ApiProblem("entity_file_not_found", $"Entity file '{role}' for '{id}' was not found."));
        }

        if (TrySplitArchiveEntry(content.Path, out var archivePath, out var memberPath)) {
            var archiveStream = await OpenArchiveEntryAsync(archivePath, memberPath, cancellationToken);
            return archiveStream is null
                ? Results.NotFound(new ApiProblem("entity_file_not_found", $"Entity file '{role}' for '{id}' was not found."))
                : Results.File(archiveStream, content.ContentType, enableRangeProcessing: true);
        }

        if (!File.Exists(content.Path)) {
            return Results.NotFound(new ApiProblem("entity_file_not_found", $"Entity file '{role}' for '{id}' was not found."));
        }

        return Results.File(
            File.OpenRead(content.Path),
            content.ContentType,
            enableRangeProcessing: true);
    }

    private static bool TrySplitArchiveEntry(
        string path,
        out string archivePath,
        out string memberPath) {
        var parts = path.Split("::", 2, StringSplitOptions.None);
        archivePath = parts.Length == 2 ? parts[0] : string.Empty;
        memberPath = parts.Length == 2 ? parts[1] : string.Empty;
        return parts.Length == 2 &&
               !string.IsNullOrWhiteSpace(archivePath) &&
               !string.IsNullOrWhiteSpace(memberPath);
    }

    private static async Task<Stream?> OpenArchiveEntryAsync(
        string archivePath,
        string memberPath,
        CancellationToken cancellationToken) {
        if (!File.Exists(archivePath)) {
            return null;
        }

        await using var archiveFile = File.OpenRead(archivePath);
        using var archive = new ZipArchive(archiveFile, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry(memberPath);
        if (entry is null) {
            return null;
        }

        var output = new MemoryStream();
        await using (var entryStream = entry.Open()) {
            await entryStream.CopyToAsync(output, cancellationToken);
        }

        output.Position = 0;
        return output;
    }
}
