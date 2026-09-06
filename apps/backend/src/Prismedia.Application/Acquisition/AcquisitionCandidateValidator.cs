using Prismedia.Application.Settings;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Applies current request and ownership facts to profile rules at both search and automatic grab boundaries.</summary>
public static class AcquisitionRuleContext {
    /// <summary>Builds the same decision context without encoding request-specific facts into a saved profile.</summary>
    public static BookAcquisitionRules Apply(BookAcquisitionRules rules, AcquisitionSearchInput input,
        UpgradeOwnedQuality? owned, ProperDownloadPolicy properPolicy, IReadOnlyList<DownloadProtocol> protocols) {
        rules = rules with {
            TargetTitle = input.WorkTitle,
            TargetAlternativeTitles = input.AlternativeWorkTitles,
            TargetEpisodeTitle = input.EpisodeNumber is null ? null : input.Title,
            TargetAbsoluteEpisodeNumber = input.EpisodeNumber is null ? null : input.AbsoluteEpisodeNumber,
            TargetYear = input.Year, TargetAuthor = input.Author, BookRendition = input.BookRendition,
            ProperPolicy = properPolicy, AllowedProtocols = protocols
        };
        if (input.SeasonNumber is not null) rules = rules with { SeasonNumber = input.SeasonNumber, EpisodeNumber = input.EpisodeNumber };
        if (input.VolumeNumber is not null) rules = rules with { VolumeNumber = input.VolumeNumber };
        return owned is null ? rules : rules with {
            IsUpgradeSearch = true, OwnedQuality = owned.BookRank ?? default, OwnedMediaQuality = owned.MediaQualityCode,
            OwnedMediaRevision = owned.MediaRevision, OwnedFormatScore = owned.FormatScore, OwnedHasSubtitles = owned.HasSubtitles
        };
    }
}

/// <summary>Rechecks a stored candidate before a new automatic downloader handoff.</summary>
public interface IAcquisitionCandidateValidator {
    /// <summary>Returns current rejection reasons; explicit manual grabs and existing handoff reconciliation bypass this boundary.</summary>
    Task<IReadOnlyList<ReleaseRejectionReason>> ValidateAsync(Guid acquisitionId, AcquisitionQueueCandidate candidate, CancellationToken cancellationToken);
}

/// <summary>Uses the search decision engine with fresh profile, request, protocol, and owned-quality facts.</summary>
public sealed class AcquisitionCandidateValidator(
    IAcquisitionStore acquisitions, IBookAcquisitionProfileStore profiles, IDownloadClientConfigStore downloadClients,
    IAcquisitionPolicyRegistry policies, SettingsService settings) : IAcquisitionCandidateValidator {
    /// <inheritdoc />
    public async Task<IReadOnlyList<ReleaseRejectionReason>> ValidateAsync(Guid acquisitionId, AcquisitionQueueCandidate candidate, CancellationToken cancellationToken) {
        var input = await acquisitions.GetSearchInputAsync(acquisitionId, cancellationToken)
            ?? throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionNotFound, "The request no longer exists.");
        var rules = AcquisitionRuleContext.Apply(
            await profiles.GetRulesAsync(input.ProfileId, input.Kind, cancellationToken), input,
            await acquisitions.GetUpgradeOwnedQualityAsync(acquisitionId, cancellationToken),
            (await settings.GetProperDownloadSettingsAsync(cancellationToken)).Policy,
            await downloadClients.GetEnabledProtocolsAsync(cancellationToken));
        var release = new IndexerRelease(candidate.Title, candidate.SizeBytes, candidate.Seeders, candidate.Peers,
            candidate.Protocol, candidate.DownloadUrl, candidate.MagnetUrl, candidate.InfoHash, candidate.InfoUrl,
            candidate.Language, candidate.PublishedAt);
        var result = policies.Get(input.Kind).DecisionEngineFor(input.Kind).Evaluate(
            [(release, candidate.IndexerConfigId, candidate.IndexerName)], rules, new HashSet<string>()).SingleOrDefault();
        return result is { Accepted: true } ? [] : result?.Rejections is { Count: > 0 } reasons
            ? reasons : [ReleaseRejectionReason.UnsupportedFormat];
    }
}
