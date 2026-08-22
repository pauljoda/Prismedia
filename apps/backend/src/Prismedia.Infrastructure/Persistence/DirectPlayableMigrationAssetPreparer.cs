using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prismedia.Application.Files;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;

namespace Prismedia.Infrastructure.Persistence;

/// <summary>
/// Prepares filesystem-backed data that PostgreSQL cannot safely classify or relocate while the
/// direct-playable Entity migration is pending. Copies are intentionally non-destructive: the old
/// cache files remain available if the database transaction is rejected.
/// </summary>
internal static class DirectPlayableMigrationAssetPreparer {
    internal const string MigrationId = "20260731190249_MigrateDirectPlayableEntities";
    internal const string PreviousMigrationId = "20260731170821_NormalizeEntityLibraryRootsAndSubtitleState";
    internal const string AdvisoryLockName = "prismedia:direct-playable-v1";
    internal const string ManifestTable = "prismedia_direct_playable_manifest";
    internal const string SourceClassificationSubject = "source-classification";
    internal const string MovieMappingSubject = "movie-mapping";
    internal const string MoviePayloadSubject = "movie-payload";
    internal const string SubtitleSubject = "entity-subtitle";
    internal const string EntityFileSubject = "entity-file";
    internal const string LibraryRootBackfillSubject = "library-root-backfill";
    internal const string LibraryRootSnapshotSubject = "library-root-snapshot";
    internal const string FileClassification = "file";
    internal const string FolderClassification = "folder";
    private const string AncestorConfirmedClassification = "ancestor-confirmed";
    private const string FilesystemClassification = "filesystem";
    private const string FilesystemOverrideClassification = "filesystem-override";
    private const string LegacyImageArchiveBookFormat = "image-archive";

    /// <summary>
    /// Classifies legacy structural source paths and copies managed child assets to the Movie id
    /// that will survive the database migration. The resulting temporary manifest is scoped to the
    /// already-open EF connection and consumed atomically by the migration SQL.
    /// </summary>
    public static async Task PrepareAsync(
        PrismediaDbContext db,
        AssetPathService assets,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(assets);
        if (db.Database.GetDbConnection() is not NpgsqlConnection connection ||
            connection.State != System.Data.ConnectionState.Open) {
            throw new InvalidOperationException(
                "Direct-playable migration preparation requires an open PostgreSQL EF connection.");
        }

        await ValidateLegacyTopologyAsync(connection, cancellationToken);
        var mappings = await ReadMovieMappingsAsync(connection, cancellationToken);
        var payloads = await ValidateMoviePayloadSourcesAsync(connection, mappings, cancellationToken);
        var classifications = await ClassifyStructuralSourcesAsync(connection, cancellationToken);
        classifications.AddRange(payloads);
        classifications.AddRange(await ReadLibraryRootBackfillsAsync(connection, cancellationToken));
        var relocations = await ReadSubtitleRelocationsAsync(
            connection,
            mappings,
            assets,
            cancellationToken);
        relocations.AddRange(await ReadArtworkRelocationsAsync(
            connection,
            mappings,
            assets,
            cancellationToken));

        foreach (var relocation in relocations
                     .DistinctBy(item => (item.SourcePath, item.DestinationPath))) {
            await CopyWithoutReplacingAsync(
                relocation.SourcePath,
                relocation.DestinationPath,
                assets.CacheRoot,
                cancellationToken);
        }

        await WriteManifestAsync(connection, mappings, classifications, relocations, cancellationToken);
    }

    private static async Task<List<ManifestEntry>> ReadLibraryRootBackfillsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken) {
        var roots = new List<LibraryRootPathCandidate>();
        await using (var rootsCommand = new NpgsqlCommand(
            "SELECT id, path FROM library_roots ORDER BY id",
            connection)) {
            await using var rootsReader = await rootsCommand.ExecuteReaderAsync(cancellationToken);
            while (await rootsReader.ReadAsync(cancellationToken)) {
                roots.Add(new LibraryRootPathCandidate(rootsReader.GetGuid(0), rootsReader.GetString(1)));
            }
        }

        await using var command = new NpgsqlCommand(
            """
            SELECT
                entity.id,
                entity.kind_code,
                array_agg(file.id ORDER BY file.id) AS source_ids,
                array_agg(file.path ORDER BY file.id) AS source_paths,
                (
                    WITH RECURSIVE ancestors(id, depth) AS (
                        SELECT entity.parent_entity_id, 1
                        WHERE entity.parent_entity_id IS NOT NULL
                        UNION ALL
                        SELECT parent.parent_entity_id, ancestors.depth + 1
                        FROM ancestors
                        INNER JOIN entities AS parent ON parent.id = ancestors.id
                        WHERE parent.parent_entity_id IS NOT NULL
                    )
                    SELECT rooted.library_root_id
                    FROM ancestors
                    INNER JOIN entity_library_roots AS rooted ON rooted.entity_id = ancestors.id
                    WHERE rooted.library_root_id IS NOT NULL
                    ORDER BY ancestors.depth
                    LIMIT 1
                ) AS nearest_ancestor_root_id
            FROM entities AS entity
            INNER JOIN entity_files AS file
                ON file.entity_id = entity.id AND file.role = @source_role
            LEFT JOIN entity_library_roots AS direct_root ON direct_root.entity_id = entity.id
            WHERE entity.kind_code = ANY(@kind_codes)
              AND direct_root.library_root_id IS NULL
            GROUP BY entity.id, entity.kind_code, entity.parent_entity_id
            ORDER BY entity.id
            """,
            connection);
        command.Parameters.AddWithValue("source_role", EntityFileRole.Source.ToCode());
        command.Parameters.AddWithValue("kind_codes", new[] {
            EntityKind.Image.ToCode(),
            EntityKind.AudioTrack.ToCode()
        });

        var candidates = new List<LibraryRootBackfillCandidate>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken)) {
            while (await reader.ReadAsync(cancellationToken)) {
                candidates.Add(new LibraryRootBackfillCandidate(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetFieldValue<Guid[]>(2),
                    reader.GetFieldValue<string[]>(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4)));
            }
        }

        var entries = roots.Select(root => new ManifestEntry(
                LibraryRootSnapshotSubject,
                root.Id,
                "path",
                root.Id,
                root.Id,
                root.Path,
                root.Path,
                Classification: null))
            .ToList();
        foreach (var candidate in candidates) {
            if (candidate.SourceIds.Length != 1 || candidate.SourcePaths.Length != 1) {
                throw new InvalidOperationException(
                    $"Source-backed {candidate.KindCode} Entity {candidate.EntityId} must own exactly one source file before library-root backfill.");
            }

            var sourcePath = candidate.SourcePaths[0];
            var resolvedRootId = LibraryRootOwnershipResolver.Resolve(sourcePath, roots);
            var classification = candidate.NearestAncestorRootId == resolvedRootId
                ? AncestorConfirmedClassification
                : candidate.NearestAncestorRootId is null
                    ? FilesystemClassification
                    : FilesystemOverrideClassification;
            entries.Add(new ManifestEntry(
                LibraryRootBackfillSubject,
                candidate.EntityId,
                "library_root_id",
                candidate.EntityId,
                candidate.EntityId,
                sourcePath,
                resolvedRootId.ToString(),
                classification));
        }

        return entries;
    }

    private static async Task<List<ManifestEntry>> ValidateMoviePayloadSourcesAsync(
        NpgsqlConnection connection,
        IReadOnlyDictionary<Guid, Guid> mappings,
        CancellationToken cancellationToken) {
        if (mappings.Count == 0) {
            return [];
        }

        await using var command = new NpgsqlCommand(
            """
            SELECT file.id, file.entity_id, file.path
            FROM entity_files AS file
            WHERE file.entity_id = ANY(@entity_ids) AND file.role = @source_role
            ORDER BY file.entity_id, file.id
            """,
            connection);
        command.Parameters.AddWithValue("entity_ids", mappings.Keys.ToArray());
        command.Parameters.AddWithValue("source_role", EntityFileRole.Source.ToCode());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var sources = new Dictionary<Guid, List<(Guid FileId, string Path)>>();
        while (await reader.ReadAsync(cancellationToken)) {
            var entityId = reader.GetGuid(1);
            if (!sources.TryGetValue(entityId, out var entitySources)) {
                entitySources = [];
                sources.Add(entityId, entitySources);
            }
            entitySources.Add((reader.GetGuid(0), reader.GetString(2)));
        }

        var entries = new List<ManifestEntry>(mappings.Count);
        foreach (var mapping in mappings) {
            if (!sources.TryGetValue(mapping.Key, out var entitySources) || entitySources.Count != 1) {
                throw new InvalidOperationException(
                    $"Legacy Movie Video {mapping.Key} must own exactly one source file before direct-playable migration.");
            }

            var source = entitySources[0];
            // The wrapper topology makes this row a file even when its media mount is offline. Only
            // observable contradictions are unsafe; absence alone must not block a schema upgrade.
            if (Directory.Exists(source.Path)) {
                throw new InvalidOperationException(
                    $"Legacy Movie Video {mapping.Key} source is a directory instead of a file: {source.Path}");
            }
            if (TryGetExistingAttributes(source.Path, out var attributes)
                && attributes.HasFlag(FileAttributes.ReparsePoint)) {
                throw new InvalidOperationException(
                    $"Legacy Movie Video {mapping.Key} source must be an ordinary file: {source.Path}");
            }

            entries.Add(new ManifestEntry(
                MoviePayloadSubject,
                source.FileId,
                "path",
                mapping.Key,
                mapping.Value,
                source.Path,
                source.Path,
                FileClassification));
        }

        return entries;
    }

    private static async Task ValidateLegacyTopologyAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken) {
        await using var command = new NpgsqlCommand(
            """
            SELECT violation
            FROM (
                SELECT 'a legacy Video has an unsupported parent kind' AS violation
                WHERE EXISTS (
                    SELECT 1
                    FROM entities AS child
                    INNER JOIN entities AS parent ON parent.id = child.parent_entity_id
                    WHERE child.kind_code = @video_kind
                      AND parent.kind_code NOT IN (@movie_kind, @series_kind, @season_kind))
                UNION ALL
                SELECT 'a Movie has multiple Video children or a non-Video child'
                WHERE EXISTS (
                    SELECT 1
                    FROM entities AS movie
                    INNER JOIN entities AS child ON child.parent_entity_id = movie.id
                    WHERE movie.kind_code = @movie_kind
                    GROUP BY movie.id
                    HAVING count(*) FILTER (WHERE child.kind_code = @video_kind) > 1
                        OR count(*) FILTER (WHERE child.kind_code <> @video_kind) > 0)
                UNION ALL
                SELECT 'a mapped legacy Video still owns child Entities'
                WHERE EXISTS (
                    SELECT 1
                    FROM entities AS descendant
                    INNER JOIN entities AS child ON child.id = descendant.parent_entity_id
                    INNER JOIN entities AS parent ON parent.id = child.parent_entity_id
                    WHERE child.kind_code = @video_kind
                      AND parent.kind_code IN (@movie_kind, @series_kind, @season_kind))
                UNION ALL
                SELECT 'a mapped legacy Video or collapsing Movie has an active lifecycle claim'
                WHERE EXISTS (
                    SELECT 1
                    FROM entities AS child
                    INNER JOIN entities AS parent ON parent.id = child.parent_entity_id
                    WHERE child.kind_code = @video_kind
                      AND parent.kind_code IN (@movie_kind, @series_kind, @season_kind)
                      AND (child.lifecycle_claim_id IS NOT NULL
                           OR (parent.kind_code = @movie_kind AND parent.lifecycle_claim_id IS NOT NULL)))
            ) AS violations
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("video_kind", EntityKind.Video.ToCode());
        command.Parameters.AddWithValue("movie_kind", EntityKind.Movie.ToCode());
        command.Parameters.AddWithValue("series_kind", EntityKind.VideoSeries.ToCode());
        command.Parameters.AddWithValue("season_kind", EntityKind.VideoSeason.ToCode());
        var violation = (string?)await command.ExecuteScalarAsync(cancellationToken);
        if (violation is not null) {
            throw new InvalidOperationException(
                $"Direct-playable migration cannot prepare filesystem assets because {violation}.");
        }
    }

    private static async Task<IReadOnlyDictionary<Guid, Guid>> ReadMovieMappingsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken) {
        await using var command = new NpgsqlCommand(
            """
            SELECT child.id, movie.id
            FROM entities AS child
            INNER JOIN entities AS movie ON movie.id = child.parent_entity_id
            WHERE child.kind_code = @video_kind AND movie.kind_code = @movie_kind
            """,
            connection);
        command.Parameters.AddWithValue("video_kind", EntityKind.Video.ToCode());
        command.Parameters.AddWithValue("movie_kind", EntityKind.Movie.ToCode());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var mappings = new Dictionary<Guid, Guid>();
        while (await reader.ReadAsync(cancellationToken)) {
            mappings.Add(reader.GetGuid(0), reader.GetGuid(1));
        }

        return mappings;
    }

    private static async Task<List<ManifestEntry>> ClassifyStructuralSourcesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken) {
        // At the previous schema version these kinds used Source rows only as folder provenance.
        // Their persisted meaning is therefore authoritative when the old mount is unavailable.
        var folderOnlyKinds = new[] {
            EntityKind.Movie.ToCode(),
            EntityKind.VideoSeries.ToCode(),
            EntityKind.VideoSeason.ToCode(),
            EntityKind.Gallery.ToCode(),
            EntityKind.AudioLibrary.ToCode(),
            EntityKind.MusicArtist.ToCode(),
            EntityKind.BookAuthor.ToCode(),
            EntityKind.BookVolume.ToCode()
        };
        var structuralKinds = folderOnlyKinds
            .Append(EntityKind.Book.ToCode())
            .ToArray();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                file.id,
                file.entity_id,
                file.path,
                entity.kind_code,
                EXISTS (
                    SELECT 1
                    FROM entity_sources AS source
                    WHERE source.entity_id = entity.id
                      AND source.code = @folder_source_code
                      AND source.value = file.path
                ) AS has_folder_provenance,
                EXISTS (
                    SELECT 1 FROM entity_file_fingerprints AS fingerprint
                    WHERE fingerprint.entity_file_id = file.id
                    UNION ALL
                    SELECT 1 FROM media_sources AS media_source
                    WHERE media_source.entity_file_id = file.id
                ) AS has_playable_file_reference,
                detail.format AS book_format
            FROM entity_files AS file
            INNER JOIN entities AS entity ON entity.id = file.entity_id
            LEFT JOIN book_details AS detail ON detail.entity_id = entity.id
            WHERE file.role = @source_role AND entity.kind_code = ANY(@structural_kinds)
            ORDER BY file.id
            """,
            connection);
        command.Parameters.AddWithValue("source_role", EntityFileRole.Source.ToCode());
        command.Parameters.AddWithValue("structural_kinds", structuralKinds);
        command.Parameters.AddWithValue("folder_source_code", EntitySourceCode.Folder.ToCode());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<ManifestEntry>();
        while (await reader.ReadAsync(cancellationToken)) {
            var fileId = reader.GetGuid(0);
            var entityId = reader.GetGuid(1);
            var path = reader.GetString(2);
            var kindCode = reader.GetString(3);
            var hasFolderProvenance = reader.GetBoolean(4);
            var hasPlayableFileReference = reader.GetBoolean(5);
            var bookFormat = reader.IsDBNull(6) ? null : reader.GetString(6);
            var isKnownFolder = folderOnlyKinds.Contains(kindCode, StringComparer.Ordinal)
                || hasFolderProvenance
                || kindCode == EntityKind.Book.ToCode()
                && bookFormat is not null
                && (bookFormat == BookFormat.Audio.ToCode()
                    || bookFormat == LegacyImageArchiveBookFormat
                    && !SupportedExtensions.ComicArchive.Contains(Path.GetExtension(path)));
            var isKnownFile = hasPlayableFileReference
                || kindCode == EntityKind.Book.ToCode()
                && bookFormat is not null
                && (bookFormat == BookFormat.Epub.ToCode()
                    || bookFormat == BookFormat.Pdf.ToCode()
                    || bookFormat == LegacyImageArchiveBookFormat
                    && SupportedExtensions.ComicArchive.Contains(Path.GetExtension(path)));
            if (isKnownFolder && isKnownFile) {
                throw new InvalidOperationException(
                    $"Legacy source '{path}' for {kindCode} Entity {entityId} has conflicting persisted ownership metadata.");
            }

            var isFile = File.Exists(path);
            var isFolder = Directory.Exists(path);
            if ((isFile && isKnownFolder) || (isFolder && isKnownFile)) {
                throw new InvalidOperationException(
                    $"Legacy source '{path}' for {kindCode} Entity {entityId} contradicts its persisted storage semantics.");
            }
            isKnownFile |= isFile;
            isKnownFolder |= isFolder;
            if (isKnownFolder == isKnownFile) {
                throw new InvalidOperationException(
                    $"Cannot infer legacy source '{path}' for {kindCode} Entity {entityId} from persisted ownership metadata.");
            }

            entries.Add(new ManifestEntry(
                SourceClassificationSubject,
                fileId,
                "path",
                entityId,
                entityId,
                path,
                path,
                isKnownFile ? FileClassification : FolderClassification));
        }

        return entries;
    }

    private static bool TryGetExistingAttributes(string path, out FileAttributes attributes) {
        try {
            attributes = File.GetAttributes(path);
            return true;
        } catch (FileNotFoundException) {
            attributes = default;
            return false;
        } catch (DirectoryNotFoundException) {
            attributes = default;
            return false;
        }
    }

    private static async Task<List<AssetRelocation>> ReadSubtitleRelocationsAsync(
        NpgsqlConnection connection,
        IReadOnlyDictionary<Guid, Guid> mappings,
        AssetPathService assets,
        CancellationToken cancellationToken) {
        if (mappings.Count == 0) {
            return [];
        }

        await using var command = new NpgsqlCommand(
            """
            SELECT id, entity_id, storage_path, source_path
            FROM entity_subtitles
            WHERE entity_id = ANY(@entity_ids)
            ORDER BY id
            """,
            connection);
        command.Parameters.AddWithValue("entity_ids", mappings.Keys.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var relocations = new List<AssetRelocation>();
        while (await reader.ReadAsync(cancellationToken)) {
            var rowId = reader.GetGuid(0);
            var oldEntityId = reader.GetGuid(1);
            var newEntityId = mappings[oldEntityId];
            AddSubtitleRelocation(
                relocations,
                assets,
                rowId,
                "storage_path",
                oldEntityId,
                newEntityId,
                reader.GetString(2));
            if (!reader.IsDBNull(3)) {
                AddSubtitleRelocation(
                    relocations,
                    assets,
                    rowId,
                    "source_path",
                    oldEntityId,
                    newEntityId,
                    reader.GetString(3));
            }
        }

        return relocations;
    }

    private static void AddSubtitleRelocation(
        ICollection<AssetRelocation> relocations,
        AssetPathService assets,
        Guid rowId,
        string column,
        Guid oldEntityId,
        Guid newEntityId,
        string path) {
        if (!LooksLikeManagedSubtitlePath(assets, oldEntityId, path)) {
            if (LooksLikeUnsafeManagedSubtitlePath(assets, oldEntityId, path)) {
                throw new InvalidOperationException(
                    $"Managed subtitle path does not match the safe direct-file convention: {path}");
            }
            return;
        }
        if (!assets.IsSubtitleAssetPath(oldEntityId, path)) {
            throw new InvalidOperationException($"Managed subtitle path is unsafe for migration: {path}");
        }

        var destinationDirectory = assets.EnsureSubtitleDirectorySafe(newEntityId);
        var destination = Path.Combine(destinationDirectory, Path.GetFileName(path));
        relocations.Add(new AssetRelocation(
            SubtitleSubject,
            rowId,
            column,
            oldEntityId,
            newEntityId,
            path,
            destination,
            path,
            destination));
    }

    private static bool LooksLikeManagedSubtitlePath(AssetPathService assets, Guid entityId, string path) {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) {
            return false;
        }

        try {
            return FileSystemPathComparison.Equals(
                Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty,
                Path.GetFullPath(assets.SubtitleDir(entityId)));
        } catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException) {
            return false;
        }
    }

    private static bool LooksLikeUnsafeManagedSubtitlePath(
        AssetPathService assets,
        Guid entityId,
        string path) {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) {
            return false;
        }

        try {
            var relative = Path.GetRelativePath(assets.CacheRoot, Path.GetFullPath(path));
            if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal)) {
                return false;
            }

            var segments = relative.Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries);
            return segments.Length >= 2 &&
                string.Equals(segments[0], AssetPaths.Videos, StringComparison.Ordinal) &&
                Guid.TryParse(segments[1], out var pathEntityId) &&
                pathEntityId == entityId;
        } catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException) {
            return false;
        }
    }

    private static async Task<List<AssetRelocation>> ReadArtworkRelocationsAsync(
        NpgsqlConnection connection,
        IReadOnlyDictionary<Guid, Guid> mappings,
        AssetPathService assets,
        CancellationToken cancellationToken) {
        if (mappings.Count == 0) {
            return [];
        }

        await using var command = new NpgsqlCommand(
            """
            SELECT id, entity_id, path
            FROM entity_files
            WHERE entity_id = ANY(@entity_ids) AND source = @custom_source
            ORDER BY id
            """,
            connection);
        command.Parameters.AddWithValue("entity_ids", mappings.Keys.ToArray());
        command.Parameters.AddWithValue("custom_source", FileSourceKind.Custom.ToCode());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var relocations = new List<AssetRelocation>();
        while (await reader.ReadAsync(cancellationToken)) {
            var rowId = reader.GetGuid(0);
            var oldEntityId = reader.GetGuid(1);
            var oldUrl = reader.GetString(2);
            if (!TryRelocateArtworkUrl(
                    oldUrl,
                    oldEntityId,
                    mappings[oldEntityId],
                    assets,
                    out var newUrl,
                    out var source,
                    out var destination)) {
                continue;
            }

            relocations.Add(new AssetRelocation(
                EntityFileSubject,
                rowId,
                "path",
                oldEntityId,
                mappings[oldEntityId],
                oldUrl,
                newUrl,
                source,
                destination));
        }

        return relocations;
    }

    private static bool TryRelocateArtworkUrl(
        string url,
        Guid oldEntityId,
        Guid newEntityId,
        AssetPathService assets,
        out string newUrl,
        out string source,
        out string destination) {
        newUrl = source = destination = string.Empty;
        if (!url.StartsWith(AssetPaths.AssetsUrlPrefix, StringComparison.Ordinal)) {
            return false;
        }

        var segments = url[AssetPaths.AssetsUrlPrefix.Length..]
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var isManagedArtworkRoot = segments.Length >= 2 &&
            (string.Equals(segments[0], AssetPaths.Custom, StringComparison.Ordinal) ||
             string.Equals(segments[0], AssetPaths.Plugins, StringComparison.Ordinal)) &&
            string.Equals(segments[1], AssetPaths.Artwork, StringComparison.Ordinal);
        if (!isManagedArtworkRoot) {
            return false;
        }
        if (segments.Length != 4 ||
            !Guid.TryParse(segments[2], out var pathEntityId) ||
            pathEntityId != oldEntityId ||
            string.IsNullOrWhiteSpace(segments[3])) {
            throw new InvalidOperationException(
                $"Managed artwork URL does not match its owning legacy Video: {url}");
        }

        segments[2] = newEntityId.ToString();
        newUrl = AssetPaths.AssetsUrlPrefix + string.Join('/', segments);
        source = assets.ResolveAssetDiskPath(url)
            ?? throw new InvalidOperationException($"Managed artwork path escapes the cache root: {url}");
        destination = assets.ResolveAssetDiskPath(newUrl)
            ?? throw new InvalidOperationException($"Managed artwork destination escapes the cache root: {newUrl}");
        return true;
    }

    private static async Task CopyWithoutReplacingAsync(
        string source,
        string destination,
        string cacheRoot,
        CancellationToken cancellationToken) {
        EnsureSafeExistingFileChain(cacheRoot, source);
        EnsureSafeDirectoryChain(cacheRoot, Path.GetDirectoryName(destination)!);
        if (File.Exists(destination)) {
            EnsureOrdinaryFile(destination, "destination");
            if (!await FilesMatchAsync(source, destination, cancellationToken)) {
                throw new InvalidOperationException(
                    $"Migration asset destination contains different bytes: {destination}");
            }
            return;
        }

        var temporary = Path.Combine(
            Path.GetDirectoryName(destination)!,
            $".{Path.GetFileName(destination)}.prismedia-migration-{Guid.NewGuid():N}.tmp");
        try {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                await using var output = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            if (!await FilesMatchAsync(source, temporary, cancellationToken)) {
                throw new IOException($"Migration asset copy verification failed: {source}");
            }
            File.Move(temporary, destination);
        } finally {
            if (File.Exists(temporary)) {
                File.Delete(temporary);
            }
        }
    }

    private static async Task<bool> FilesMatchAsync(
        string first,
        string second,
        CancellationToken cancellationToken) {
        if (new FileInfo(first).Length != new FileInfo(second).Length) {
            return false;
        }

        var firstHash = await HashAsync(first, cancellationToken);
        var secondHash = await HashAsync(second, cancellationToken);
        return firstHash.AsSpan().SequenceEqual(secondHash);
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken cancellationToken) {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    private static void EnsureSafeDirectoryChain(string cacheRoot, string targetDirectory) {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cacheRoot));
        Directory.CreateDirectory(root);
        EnsureOrdinaryDirectory(root);
        var relative = Path.GetRelativePath(root, Path.GetFullPath(targetDirectory));
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"Migration asset destination escapes the cache root: {targetDirectory}");
        }

        var current = root;
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries)) {
            current = Path.Combine(current, segment);
            Directory.CreateDirectory(current);
            EnsureOrdinaryDirectory(current);
        }
    }

    private static void EnsureSafeExistingFileChain(string cacheRoot, string sourcePath) {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cacheRoot));
        var source = Path.GetFullPath(sourcePath);
        var relative = Path.GetRelativePath(root, source);
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"Migration asset source escapes the cache root: {sourcePath}");
        }

        EnsureOrdinaryDirectory(root);
        var current = root;
        var segments = relative.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments.SkipLast(1)) {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current)) {
                throw new DirectoryNotFoundException(
                    $"Migration asset source directory does not exist: {current}");
            }
            EnsureOrdinaryDirectory(current);
        }
        EnsureOrdinaryFile(source, "source");
    }

    private static void EnsureOrdinaryDirectory(string path) {
        var attributes = File.GetAttributes(path);
        if (!attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint)) {
            throw new IOException($"Migration asset directories must be ordinary directories: {path}");
        }
    }

    private static void EnsureOrdinaryFile(string path, string role) {
        if (!File.Exists(path)) {
            throw new FileNotFoundException($"Migration asset {role} does not exist.", path);
        }
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint)) {
            throw new IOException($"Migration asset {role} must be an ordinary file: {path}");
        }
    }

    private static async Task WriteManifestAsync(
        NpgsqlConnection connection,
        IReadOnlyDictionary<Guid, Guid> mappings,
        IReadOnlyCollection<ManifestEntry> classifications,
        IReadOnlyCollection<AssetRelocation> relocations,
        CancellationToken cancellationToken) {
        await using (var create = new NpgsqlCommand(
            $$"""
            CREATE TEMP TABLE IF NOT EXISTS {{ManifestTable}} (
                subject text NOT NULL,
                row_id uuid NOT NULL,
                column_name text NOT NULL,
                old_entity_id uuid NOT NULL,
                new_entity_id uuid NOT NULL,
                old_value text NOT NULL,
                new_value text NOT NULL,
                classification text NULL,
                PRIMARY KEY (subject, row_id, column_name)
            ) ON COMMIT PRESERVE ROWS;
            TRUNCATE TABLE {{ManifestTable}};
            """,
            connection)) {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var mappingEntries = mappings.Select(mapping => new ManifestEntry(
            MovieMappingSubject,
            mapping.Key,
            "entity_id",
            mapping.Key,
            mapping.Value,
            mapping.Key.ToString(),
            mapping.Value.ToString(),
            Classification: null));
        var entries = mappingEntries
            .Concat(classifications)
            .Concat(relocations.Select(item => item.Manifest))
            .ToArray();
        foreach (var entry in entries) {
            await using var insert = new NpgsqlCommand(
                $$"""
                INSERT INTO {{ManifestTable}}
                    (subject, row_id, column_name, old_entity_id, new_entity_id, old_value, new_value, classification)
                VALUES
                    (@subject, @row_id, @column_name, @old_entity_id, @new_entity_id, @old_value, @new_value, @classification)
                """,
                connection);
            insert.Parameters.AddWithValue("subject", entry.Subject);
            insert.Parameters.AddWithValue("row_id", entry.RowId);
            insert.Parameters.AddWithValue("column_name", entry.ColumnName);
            insert.Parameters.AddWithValue("old_entity_id", entry.OldEntityId);
            insert.Parameters.AddWithValue("new_entity_id", entry.NewEntityId);
            insert.Parameters.AddWithValue("old_value", entry.OldValue);
            insert.Parameters.AddWithValue("new_value", entry.NewValue);
            insert.Parameters.AddWithValue("classification", (object?)entry.Classification ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private sealed record ManifestEntry(
        string Subject,
        Guid RowId,
        string ColumnName,
        Guid OldEntityId,
        Guid NewEntityId,
        string OldValue,
        string NewValue,
        string? Classification);

    private sealed record LibraryRootBackfillCandidate(
        Guid EntityId,
        string KindCode,
        Guid[] SourceIds,
        string[] SourcePaths,
        Guid? NearestAncestorRootId);

    private sealed record AssetRelocation(
        string Subject,
        Guid RowId,
        string ColumnName,
        Guid OldEntityId,
        Guid NewEntityId,
        string OldValue,
        string NewValue,
        string SourcePath,
        string DestinationPath) {
        public ManifestEntry Manifest => new(
            Subject,
            RowId,
            ColumnName,
            OldEntityId,
            NewEntityId,
            OldValue,
            NewValue,
            Classification: null);
    }
}
