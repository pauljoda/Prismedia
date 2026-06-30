using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class MediaFileIgnoreRow {
    public Guid LibraryRootId { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Kind { get; set; } = FileEntryKind.File.ToCode();
    public string EntityKindCode { get; set; } = string.Empty;
    public string Reason { get; set; } = MediaFileIgnoreReason.DeletedFromLibrary.ToCode();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class UiPreferenceRow {
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BrowserSessionRow {
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class BrowserSessionSettingRow {
    public Guid BrowserSessionId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AppSettingRow {
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AppSecurityRow {
    public int Id { get; set; }
    public Guid ServerId { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public bool DefaultProfileSeeded { get; set; }
    public DateTimeOffset ApiKeyCreatedAt { get; set; }
    public DateTimeOffset ApiKeyUpdatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class JellyfinProfileRow {
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string NormalizedUsername { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool AllowSfw { get; set; } = true;
    public bool AllowNsfw { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class JellyfinSessionRow {
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string? Client { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceId { get; set; }
    public string? ApplicationVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? InvalidatedAt { get; set; }
}

public sealed class ProviderConfigRow {
    public Guid Id { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ProviderType ProviderType { get; set; } = ProviderType.Native;
    public string SettingsJson { get; set; } = "{}";
    public bool Enabled { get; set; } = true;
    public bool IsNsfw { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ProviderCredentialRow {
    public Guid Id { get; set; }
    public Guid ProviderConfigId { get; set; }
    public string CredentialKey { get; set; } = string.Empty;
    public string EncryptedValue { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class IdentifyResultRow {
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public Guid? ProviderConfigId { get; set; }
    public string Action { get; set; } = string.Empty;
    public IdentifyResultStatus Status { get; set; } = IdentifyResultStatus.Pending;
    public string? MatchType { get; set; }
    public string? RawResultJson { get; set; }
    public string? ProposedResultJson { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class IdentifyQueueItemRow {
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public IdentifyQueueState State { get; set; } = IdentifyQueueState.Search;
    public string? ProviderCode { get; set; }
    public IdentifyAction Action { get; set; } = IdentifyAction.Search;
    public string? QueryJson { get; set; }
    public string? CandidatesJson { get; set; }
    public string? ProposalJson { get; set; }
    public string? Error { get; set; }

    /// <summary>
    /// Id of the background <see cref="Prismedia.Domain.Entities.JobType.IdentifyCascade"/> run
    /// currently streaming this item's child tree, or null when none is in flight. The review screen
    /// treats a non-null value as "still resolving children" and gates Accept until it clears.
    /// </summary>
    public Guid? CascadeJobId { get; set; }

    /// <summary>
    /// Id of the <see cref="Prismedia.Domain.Entities.JobType.IdentifySearch"/> run that owns the
    /// item's pending search, or null when no search is requested or running. A newer search request
    /// cancels the old job and restamps this marker; a job whose id no longer matches must not write
    /// its result, mirroring the <see cref="CascadeJobId"/> ownership pattern.
    /// </summary>
    public Guid? SearchJobId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class FingerprintSubmissionRow {
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public Guid? ProviderConfigId { get; set; }
    public string Algorithm { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public FingerprintSubmissionStatus Status { get; set; } = FingerprintSubmissionStatus.Success;
    public string? Error { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
}
