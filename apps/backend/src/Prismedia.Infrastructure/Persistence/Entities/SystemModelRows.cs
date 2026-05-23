using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Persistence.Entities;

public sealed class MediaFileIgnoreRow {
    public string Path { get; set; } = string.Empty;
    public string EntityKindCode { get; set; } = string.Empty;
    public string Reason { get; set; } = "deleted-from-library";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class UiPreferenceRow {
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
