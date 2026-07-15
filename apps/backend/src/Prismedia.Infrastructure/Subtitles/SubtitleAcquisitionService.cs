using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Prismedia.Application.Settings;
using Prismedia.Application.Subtitles;
using Prismedia.Application.Jobs.Ports;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Media.Processing;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Persistence.Entities;

namespace Prismedia.Infrastructure.Subtitles;

/// <summary>
/// Coordinates native subtitle provider configuration, media-identity projection, safe asset import,
/// and idempotent provider-track persistence.
/// </summary>
internal sealed class SubtitleAcquisitionService(
    PrismediaDbContext db,
    OpenSubtitlesClient openSubtitles,
    SubtitleAssetImportService assets,
    IMediaHashing hashing,
    SettingsService settings,
    IConfiguration configuration) : ISubtitleAcquisitionService {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<OpenSubtitlesConfiguration> GetOpenSubtitlesConfigurationAsync(
        CancellationToken cancellationToken) {
        var stored = await LoadStoredConfigurationAsync(cancellationToken);
        var credentials = await LoadCredentialsAsync(cancellationToken);
        return ToSafeConfiguration(stored, credentials);
    }

    public async Task<OpenSubtitlesConfiguration> SaveOpenSubtitlesConfigurationAsync(
        SaveOpenSubtitlesConfiguration configurationUpdate,
        CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var row = await db.ProviderConfigs
            .SingleOrDefaultAsync(candidate => candidate.ProviderCode == SubtitleProviderCodes.OpenSubtitles, cancellationToken);
        if (row is null) {
            row = new ProviderConfigRow {
                Id = Guid.NewGuid(),
                ProviderCode = SubtitleProviderCodes.OpenSubtitles,
                DisplayName = "OpenSubtitles.com",
                ProviderType = ProviderType.Native,
                CreatedAt = now,
            };
            db.ProviderConfigs.Add(row);
        }

        row.Enabled = configurationUpdate.Enabled;
        row.SettingsJson = JsonSerializer.Serialize(new StoredOpenSubtitlesSettings(
            configurationUpdate.IncludeAiTranslated,
            configurationUpdate.IncludeMachineTranslated), Json);
        row.UpdatedAt = now;
        await UpsertCredentialAsync(row.Id, OpenSubtitlesCredentialKeys.ApiKey, configurationUpdate.ApiKey, now, cancellationToken);
        await UpsertCredentialAsync(row.Id, OpenSubtitlesCredentialKeys.Username, configurationUpdate.Username, now, cancellationToken);
        await UpsertCredentialAsync(row.Id, OpenSubtitlesCredentialKeys.Password, configurationUpdate.Password, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetOpenSubtitlesConfigurationAsync(cancellationToken);
    }

    public async Task<SubtitleProviderTestResult> TestOpenSubtitlesAsync(CancellationToken cancellationToken) {
        var connection = await GetConnectionAsync(requireEnabled: false, cancellationToken);
        return connection is null
            ? new SubtitleProviderTestResult(false, "OpenSubtitles API key, username, and password are required.")
            : await openSubtitles.TestAsync(connection, cancellationToken);
    }

    public async Task<IReadOnlyList<SubtitleSearchResult>> SearchAsync(
        Guid videoId,
        SubtitleSearchRequest request,
        CancellationToken cancellationToken) {
        var connection = await GetConnectionAsync(requireEnabled: true, cancellationToken)
            ?? throw new InvalidOperationException("OpenSubtitles is disabled or is missing credentials.");
        var context = await BuildSearchContextAsync(videoId, request.Languages, cancellationToken);
        try {
            return await openSubtitles.SearchAsync(connection, context, cancellationToken);
        } catch (OpenSubtitlesException exception) {
            throw new SubtitleProviderUnavailableException(exception.Message, exception);
        }
    }

    public async Task<SubtitleAcquisitionResult> AcquireAsync(
        Guid videoId,
        string provider,
        string candidateId,
        CancellationToken cancellationToken) {
        if (!string.Equals(provider, SubtitleProviderCodes.OpenSubtitles, StringComparison.Ordinal)) {
            throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown subtitle provider.");
        }

        var sourceKey = SubtitleSourceKeys.Provider(provider, candidateId);
        var existing = await db.EntitySubtitles.AsNoTracking()
            .FirstOrDefaultAsync(track => track.EntityId == videoId &&
                track.Source == EntitySubtitleSource.Provider &&
                track.SourceKey == sourceKey, cancellationToken);
        if (existing is not null) {
            return new SubtitleAcquisitionResult(existing.Id, AlreadyPresent: true);
        }

        var connection = await GetConnectionAsync(requireEnabled: true, cancellationToken)
            ?? throw new InvalidOperationException("OpenSubtitles is disabled or is missing credentials.");
        OpenSubtitlesDownloadArtifact artifact;
        try {
            artifact = await openSubtitles.DownloadAsync(connection, candidateId, cancellationToken);
        } catch (OpenSubtitlesException exception) when (
            exception.Message.Contains("candidate expired", StringComparison.OrdinalIgnoreCase)) {
            throw new SubtitleCandidateUnavailableException(exception.Message, exception);
        } catch (OpenSubtitlesException exception) {
            throw new SubtitleProviderUnavailableException(exception.Message, exception);
        }
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"prismedia-provider-subtitle-{Guid.NewGuid():N}{SubtitleFileExtensions.ForFormat(artifact.Format)}");
        ImportedSidecarSubtitleAssets? imported = null;
        try {
            await File.WriteAllBytesAsync(temporaryPath, artifact.Content, cancellationToken);
            var importedAssets = await assets.ImportAsync(
                videoId,
                temporaryPath,
                sourceKey,
                artifact.Format,
                cancellationToken);
            imported = importedAssets;
            var track = new EntitySubtitleRow {
                Id = Guid.NewGuid(),
                EntityId = videoId,
                Language = string.IsNullOrWhiteSpace(artifact.Language)
                    ? SubtitleLanguages.Undetermined
                    : artifact.Language,
                Label = $"OpenSubtitles · {artifact.ReleaseName ?? Path.GetFileNameWithoutExtension(artifact.FileName)}",
                Format = SubtitleFormats.Vtt,
                Source = EntitySubtitleSource.Provider,
                SourceKey = sourceKey,
                StoragePath = importedAssets.StoragePath,
                SourceFormat = artifact.Format,
                SourcePath = importedAssets.SourcePath,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.EntitySubtitles.Add(track);
            await db.SaveChangesAsync(cancellationToken);
            return new SubtitleAcquisitionResult(track.Id, AlreadyPresent: false);
        } catch (SubtitleAssetImportException exception) {
            if (imported is not null) {
                await assets.DeleteAsync(imported.CreatedPaths, CancellationToken.None);
            }
            throw new SubtitleImportException("The downloaded subtitle could not be imported safely.", exception);
        } catch {
            if (imported is not null) {
                await assets.DeleteAsync(imported.CreatedPaths, CancellationToken.None);
            }
            throw;
        } finally {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    public async Task<AutomaticSubtitleAcquisitionResult> AcquireMissingPreferredAsync(
        Guid videoId,
        CancellationToken cancellationToken) {
        var subtitleSettings = await settings.GetSubtitleSettingsAsync(cancellationToken);
        if (!subtitleSettings.AutoDownloadEnabled) {
            return new AutomaticSubtitleAcquisitionResult(0, [], "Automatic subtitle acquisition is disabled.");
        }

        var desired = NormalizeLanguages(subtitleSettings.AutoDownloadLanguages);
        if (desired.Count == 0) {
            return new AutomaticSubtitleAcquisitionResult(0, [], "No automatic subtitle languages are configured.");
        }

        var existing = await db.EntitySubtitles.AsNoTracking()
            .Where(track => track.EntityId == videoId)
            .Select(track => track.Language)
            .ToArrayAsync(cancellationToken);
        var existingLanguages = NormalizeLanguages(existing);
        var missing = desired.Where(language => !existingLanguages.Contains(language)).ToArray();
        if (missing.Length == 0) {
            return new AutomaticSubtitleAcquisitionResult(0, []);
        }

        IReadOnlyList<SubtitleSearchResult> results;
        try {
            results = await SearchAsync(videoId, new SubtitleSearchRequest(missing), cancellationToken);
        } catch (InvalidOperationException exception) {
            return new AutomaticSubtitleAcquisitionResult(0, missing, exception.Message);
        }

        var downloaded = 0;
        var remaining = new HashSet<string>(missing, StringComparer.OrdinalIgnoreCase);
        foreach (var language in missing) {
            var candidates = results
                .Where(result => LanguageKey(result.Language) == language &&
                    result.AutomaticEligible &&
                    result.MatchConfidence >= subtitleSettings.AutoDownloadMinimumConfidence)
                .OrderByDescending(result => result.MatchConfidence)
                .ThenByDescending(result => result.QualityScore)
                .ThenByDescending(result => result.DownloadCount)
                .ToArray();
            foreach (var candidate in candidates) {
                try {
                    await AcquireAsync(videoId, candidate.Provider, candidate.CandidateId, cancellationToken);
                    downloaded++;
                    remaining.Remove(language);
                    break;
                } catch (SubtitleCandidateUnavailableException) {
                    // Search results can expire between ranking and download. Continue to the next
                    // independently resolved candidate without weakening the automatic threshold.
                } catch (SubtitleImportException) {
                    // A malformed provider artifact is candidate-specific; a lower-ranked valid
                    // match is still preferable to leaving the language missing.
                }
            }
        }

        return new AutomaticSubtitleAcquisitionResult(downloaded, remaining.ToArray());
    }

    private async Task<OpenSubtitlesSearchContext> BuildSearchContextAsync(
        Guid videoId,
        IReadOnlyList<string> requestedLanguages,
        CancellationToken cancellationToken) {
        var entity = await db.Entities.AsNoTracking().SingleOrDefaultAsync(row => row.Id == videoId, cancellationToken)
            ?? throw new KeyNotFoundException($"Video '{videoId}' was not found.");
        if (!await db.VideoDetails.AsNoTracking().AnyAsync(row => row.EntityId == videoId, cancellationToken)) {
            throw new KeyNotFoundException($"Entity '{videoId}' is not a video.");
        }

        var source = await db.EntityFiles.AsNoTracking()
            .Where(file => file.EntityId == videoId && file.Role == EntityFileRole.Source)
            .OrderBy(file => file.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The video has no source file.");
        var hash = await db.EntityFileFingerprints.AsNoTracking()
            .Where(fingerprint => fingerprint.EntityId == videoId &&
                fingerprint.Algorithm == FingerprintAlgorithm.Oshash &&
                (fingerprint.EntityFileId == source.Id || fingerprint.EntityFileId == null))
            .OrderByDescending(fingerprint => fingerprint.EntityFileId == source.Id)
            .Select(fingerprint => fingerprint.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(hash)) {
            var computed = await hashing.ComputeHashesAsync(source.Path, computeMd5: false, cancellationToken);
            hash = computed.Oshash;
            db.EntityFileFingerprints.Add(new EntityFileFingerprintRow {
                Id = Guid.NewGuid(),
                EntityId = videoId,
                EntityFileId = source.Id,
                Algorithm = FingerprintAlgorithm.Oshash,
                Value = hash,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        var ancestry = await LoadAncestryAsync(entity, cancellationToken);
        var ancestorIds = ancestry.Select(row => row.Id).ToArray();
        var externalIds = await db.EntityExternalIds.AsNoTracking()
            .Where(row => ancestorIds.Contains(row.EntityId))
            .ToArrayAsync(cancellationToken);
        var positions = await db.EntityPositions.AsNoTracking()
            .Where(row => row.EntityId == videoId &&
                (row.Code == EntityPositionCodes.Season || row.Code == EntityPositionCodes.Episode))
            .ToDictionaryAsync(row => row.Code, row => row.Value, cancellationToken);
        var year = await db.EntityDates.AsNoTracking()
            .Where(row => ancestorIds.Contains(row.EntityId) && row.SortableValue != null)
            .OrderBy(row => Array.IndexOf(ancestorIds, row.EntityId))
            .Select(row => (int?)row.SortableValue!.Value.Year)
            .FirstOrDefaultAsync(cancellationToken);
        var currentImdb = ExternalId(externalIds, videoId, ExternalIdProviders.Imdb);
        var parentImdb = ancestry.Skip(1)
            .Select(row => ExternalId(externalIds, row.Id, ExternalIdProviders.Imdb))
            .FirstOrDefault(value => value is not null);
        var tmdbText = ExternalId(externalIds, videoId, ExternalIdProviders.Tmdb);

        return new OpenSubtitlesSearchContext(
            entity.Title,
            Path.GetFileName(source.Path),
            hash,
            currentImdb,
            parentImdb,
            int.TryParse(tmdbText, out var tmdbId) ? tmdbId : null,
            year,
            positions.GetValueOrDefault(EntityPositionCodes.Season),
            positions.GetValueOrDefault(EntityPositionCodes.Episode),
            NormalizeLanguages(requestedLanguages).ToArray());
    }

    private async Task<IReadOnlyList<EntityRow>> LoadAncestryAsync(
        EntityRow entity,
        CancellationToken cancellationToken) {
        var rows = new List<EntityRow> { entity };
        var parentId = entity.ParentEntityId;
        while (parentId is not null && rows.Count < 4) {
            var parent = await db.Entities.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == parentId, cancellationToken);
            if (parent is null) break;
            rows.Add(parent);
            parentId = parent.ParentEntityId;
        }
        return rows;
    }

    private async Task<OpenSubtitlesConnection?> GetConnectionAsync(
        bool requireEnabled,
        CancellationToken cancellationToken) {
        var stored = await LoadStoredConfigurationAsync(cancellationToken);
        if (requireEnabled && !stored.Enabled) return null;
        var credentials = await LoadCredentialsAsync(cancellationToken);
        return credentials.TryGetValue(OpenSubtitlesCredentialKeys.ApiKey, out var apiKey) &&
            credentials.TryGetValue(OpenSubtitlesCredentialKeys.Username, out var username) &&
            credentials.TryGetValue(OpenSubtitlesCredentialKeys.Password, out var password)
            ? new OpenSubtitlesConnection(
                apiKey,
                username,
                password,
                stored.Settings.IncludeAiTranslated,
                stored.Settings.IncludeMachineTranslated)
            : null;
    }

    private async Task<StoredConfiguration> LoadStoredConfigurationAsync(CancellationToken cancellationToken) {
        var row = await db.ProviderConfigs.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.ProviderCode == SubtitleProviderCodes.OpenSubtitles, cancellationToken);
        if (row is null) return new StoredConfiguration(false, new StoredOpenSubtitlesSettings(false, false));
        try {
            return new StoredConfiguration(
                row.Enabled,
                JsonSerializer.Deserialize<StoredOpenSubtitlesSettings>(row.SettingsJson, Json) ??
                    new StoredOpenSubtitlesSettings(false, false));
        } catch (JsonException) {
            return new StoredConfiguration(row.Enabled, new StoredOpenSubtitlesSettings(false, false));
        }
    }

    private async Task<Dictionary<string, string>> LoadCredentialsAsync(CancellationToken cancellationToken) {
        var providerId = await db.ProviderConfigs.AsNoTracking()
            .Where(row => row.ProviderCode == SubtitleProviderCodes.OpenSubtitles)
            .Select(row => (Guid?)row.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var stored = providerId is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : await db.ProviderCredentials.AsNoTracking()
                .Where(row => row.ProviderConfigId == providerId)
                .ToDictionaryAsync(row => row.CredentialKey, row => row.EncryptedValue, StringComparer.Ordinal, cancellationToken);
        Override(stored, OpenSubtitlesCredentialKeys.ApiKey, configuration[OpenSubtitlesProtocol.ApiKeyEnvironment]);
        Override(stored, OpenSubtitlesCredentialKeys.Username, configuration[OpenSubtitlesProtocol.UsernameEnvironment]);
        Override(stored, OpenSubtitlesCredentialKeys.Password, configuration[OpenSubtitlesProtocol.PasswordEnvironment]);
        return stored;
    }

    private async Task UpsertCredentialAsync(
        Guid providerId,
        string key,
        string? value,
        DateTimeOffset now,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(value)) return;
        var row = await db.ProviderCredentials.SingleOrDefaultAsync(
            candidate => candidate.ProviderConfigId == providerId && candidate.CredentialKey == key,
            cancellationToken);
        if (row is null) {
            row = new ProviderCredentialRow {
                Id = Guid.NewGuid(),
                ProviderConfigId = providerId,
                CredentialKey = key,
                CreatedAt = now,
            };
            db.ProviderCredentials.Add(row);
        }
        row.EncryptedValue = value.Trim();
        row.UpdatedAt = now;
    }

    private static OpenSubtitlesConfiguration ToSafeConfiguration(
        StoredConfiguration stored,
        IReadOnlyDictionary<string, string> credentials) =>
        new(
            stored.Enabled,
            credentials.ContainsKey(OpenSubtitlesCredentialKeys.ApiKey),
            credentials.ContainsKey(OpenSubtitlesCredentialKeys.Username),
            credentials.ContainsKey(OpenSubtitlesCredentialKeys.Password),
            stored.Settings.IncludeAiTranslated,
            stored.Settings.IncludeMachineTranslated);

    private static HashSet<string> NormalizeLanguages(IEnumerable<string> languages) =>
        languages.Select(LanguageKey)
            .Where(language => language.Length is >= 2 and <= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string LanguageKey(string language) =>
        language.Trim().Split('-', '_')[0].ToLowerInvariant() switch {
            "eng" => "en",
            "spa" => "es",
            "fra" or "fre" => "fr",
            "deu" or "ger" => "de",
            "ita" => "it",
            "jpn" => "ja",
            "por" => "pt",
            var value => value,
        };

    private static string? ExternalId(
        IEnumerable<EntityExternalIdRow> ids,
        Guid entityId,
        string provider) =>
        ids.FirstOrDefault(row => row.EntityId == entityId &&
            string.Equals(row.Provider, provider, StringComparison.OrdinalIgnoreCase))?.Value;

    private static void Override(IDictionary<string, string> values, string key, string? value) {
        if (!string.IsNullOrWhiteSpace(value)) values[key] = value;
    }

    private static void TryDeleteTemporaryFile(string path) {
        try {
            if (File.Exists(path)) File.Delete(path);
        } catch (IOException) {
            // Best effort: the app-owned importer never references this temporary source after return.
        } catch (UnauthorizedAccessException) {
            // Best effort cleanup only.
        }
    }

    private sealed record StoredOpenSubtitlesSettings(bool IncludeAiTranslated, bool IncludeMachineTranslated);
    private sealed record StoredConfiguration(bool Enabled, StoredOpenSubtitlesSettings Settings);
}
