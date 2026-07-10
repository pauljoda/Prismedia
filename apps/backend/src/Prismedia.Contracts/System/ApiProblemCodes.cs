namespace Prismedia.Contracts.System;

/// <summary>
/// Canonical machine-readable API problem codes. The single source of truth for the
/// <see cref="ApiProblem.Code"/> values returned by endpoints and matched by clients.
/// Per the Identifier Discipline contract, throwers and HTTP mappers reference these
/// constants instead of bare string literals.
/// </summary>
public static class ApiProblemCodes {
    /// <summary>Problem code <c>acquisition_import_blocked</c>.</summary>
    public const string AcquisitionImportBlocked = "acquisition_import_blocked";

    /// <summary>Problem code <c>acquisition_invalid</c>.</summary>
    public const string AcquisitionInvalid = "acquisition_invalid";

    /// <summary>Problem code <c>acquisition_not_found</c>.</summary>
    public const string AcquisitionNotFound = "acquisition_not_found";

    /// <summary>Problem code <c>acquisition_profile_invalid</c>.</summary>
    public const string AcquisitionProfileInvalid = "acquisition_profile_invalid";

    /// <summary>Problem code <c>acquisition_release_not_found</c>.</summary>
    public const string AcquisitionReleaseNotFound = "acquisition_release_not_found";

    /// <summary>Problem code <c>admin_required</c>.</summary>
    public const string AdminRequired = "admin_required";

    /// <summary>Problem code <c>audio_stream_not_found</c>.</summary>
    public const string AudioStreamNotFound = "audio_stream_not_found";

    /// <summary>Problem code <c>authentication_required</c>.</summary>
    public const string AuthenticationRequired = "authentication_required";

    /// <summary>Problem code <c>download_client_invalid</c>.</summary>
    public const string DownloadClientInvalid = "download_client_invalid";

    /// <summary>Problem code <c>download_client_unreachable</c>.</summary>
    public const string DownloadClientUnreachable = "download_client_unreachable";

    /// <summary>Problem code <c>indexer_invalid</c>.</summary>
    public const string IndexerInvalid = "indexer_invalid";

    /// <summary>Problem code <c>indexer_unreachable</c>.</summary>
    public const string IndexerUnreachable = "indexer_unreachable";

    /// <summary>Problem code <c>auth_rate_limited</c>.</summary>
    public const string AuthRateLimited = "auth_rate_limited";

    /// <summary>Problem code <c>changelog_not_found</c>.</summary>
    public const string ChangelogNotFound = "changelog_not_found";

    /// <summary>Problem code <c>collection_not_found</c>.</summary>
    public const string CollectionNotFound = "collection_not_found";

    /// <summary>Problem code <c>database_backup_invalid</c>.</summary>
    public const string DatabaseBackupInvalid = "database_backup_invalid";

    /// <summary>Problem code <c>database_backup_not_found</c>.</summary>
    public const string DatabaseBackupNotFound = "database_backup_not_found";

    /// <summary>Problem code <c>database_restore_invalid</c>.</summary>
    public const string DatabaseRestoreInvalid = "database_restore_invalid";

    /// <summary>Problem code <c>empty_bulk_identify</c>.</summary>
    public const string EmptyBulkIdentify = "empty_bulk_identify";

    /// <summary>Problem code <c>entity_file_not_found</c>.</summary>
    public const string EntityFileNotFound = "entity_file_not_found";

    /// <summary>Problem code <c>entity_deletion_conflict</c>.</summary>
    public const string EntityDeletionConflict = "entity_deletion_conflict";

    /// <summary>Problem code <c>entity_not_creatable</c>.</summary>
    public const string EntityNotCreatable = "entity_not_creatable";

    /// <summary>Problem code <c>entity_not_deletable</c>.</summary>
    public const string EntityNotDeletable = "entity_not_deletable";

    /// <summary>Problem code <c>entity_not_found</c>.</summary>
    public const string EntityNotFound = "entity_not_found";

    /// <summary>Problem code <c>external_identity_ambiguous</c>.</summary>
    public const string ExternalIdentityAmbiguous = "external_identity_ambiguous";

    /// <summary>Problem code <c>file_conflict</c>.</summary>
    public const string FileConflict = "file_conflict";

    /// <summary>Problem code <c>identify_apply_progress_not_found</c>.</summary>
    public const string IdentifyApplyProgressNotFound = "identify_apply_progress_not_found";

    /// <summary>Problem code <c>identify_failed</c>.</summary>
    public const string IdentifyFailed = "identify_failed";

    /// <summary>Problem code <c>identify_target_not_eligible</c>.</summary>
    public const string IdentifyTargetNotEligible = "identify_target_not_eligible";

    /// <summary>Problem code <c>identify_queue_apply_invalid</c>.</summary>
    public const string IdentifyQueueApplyInvalid = "identify_queue_apply_invalid";

    /// <summary>Problem code <c>identify_queue_item_not_found</c>.</summary>
    public const string IdentifyQueueItemNotFound = "identify_queue_item_not_found";

    /// <summary>Problem code <c>identify_queue_proposal_invalid</c>.</summary>
    public const string IdentifyQueueProposalInvalid = "identify_queue_proposal_invalid";

    /// <summary>Problem code <c>invalid_credentials</c>.</summary>
    public const string InvalidCredentials = "invalid_credentials";

    /// <summary>Problem code <c>invalid_collection</c>.</summary>
    public const string InvalidCollection = "invalid_collection";

    /// <summary>Problem code <c>invalid_collection_items</c>.</summary>
    public const string InvalidCollectionItems = "invalid_collection_items";

    /// <summary>Problem code <c>invalid_collection_rules</c>.</summary>
    public const string InvalidCollectionRules = "invalid_collection_rules";

    /// <summary>Problem code <c>invalid_entity</c>.</summary>
    public const string InvalidEntity = "invalid_entity";

    /// <summary>Problem code <c>invalid_entity_image_upload</c>.</summary>
    public const string InvalidEntityImageUpload = "invalid_entity_image_upload";

    /// <summary>Problem code <c>invalid_entity_kind</c>.</summary>
    public const string InvalidEntityKind = "invalid_entity_kind";

    /// <summary>Problem code <c>invalid_entity_metadata_patch</c>.</summary>
    public const string InvalidEntityMetadataPatch = "invalid_entity_metadata_patch";

    /// <summary>Problem code <c>invalid_path</c>.</summary>
    public const string InvalidPath = "invalid_path";

    /// <summary>Problem code <c>invalid_opds_request</c>.</summary>
    public const string InvalidOpdsRequest = "invalid_opds_request";

    /// <summary>Problem code <c>invalid_playback_event_kind</c>.</summary>
    public const string InvalidPlaybackEventKind = "invalid_playback_event_kind";

    /// <summary>Problem code <c>invalid_playback_statistics_window</c>.</summary>
    public const string InvalidPlaybackStatisticsWindow = "invalid_playback_statistics_window";

    /// <summary>Problem code <c>invalid_upload</c>.</summary>
    public const string InvalidUpload = "invalid_upload";

    /// <summary>Problem code <c>jellyfin_audio_not_found</c>.</summary>
    public const string JellyfinAudioNotFound = "jellyfin_audio_not_found";

    /// <summary>Problem code <c>jellyfin_auth_failed</c>.</summary>
    public const string JellyfinAuthFailed = "jellyfin_auth_failed";

    /// <summary>Problem code <c>jellyfin_image_not_found</c>.</summary>
    public const string JellyfinImageNotFound = "jellyfin_image_not_found";

    /// <summary>Problem code <c>jellyfin_item_file_not_found</c>.</summary>
    public const string JellyfinItemFileNotFound = "jellyfin_item_file_not_found";

    /// <summary>Problem code <c>jellyfin_item_not_found</c>.</summary>
    public const string JellyfinItemNotFound = "jellyfin_item_not_found";

    /// <summary>Problem code <c>jellyfin_quick_connect_disabled</c>.</summary>
    public const string JellyfinQuickConnectDisabled = "jellyfin_quick_connect_disabled";

    /// <summary>Problem code <c>jellyfin_quick_connect_not_found</c>.</summary>
    public const string JellyfinQuickConnectNotFound = "jellyfin_quick_connect_not_found";

    /// <summary>Problem code <c>jellyfin_user_not_found</c>.</summary>
    public const string JellyfinUserNotFound = "jellyfin_user_not_found";

    /// <summary>Problem code <c>last_admin_required</c>.</summary>
    public const string LastAdminRequired = "last_admin_required";

    /// <summary>Problem code <c>not_found</c>.</summary>
    public const string NotFound = "not_found";

    /// <summary>Problem code <c>password_invalid</c>.</summary>
    public const string PasswordInvalid = "password_invalid";

    /// <summary>Problem code <c>playback_item_not_found</c>.</summary>
    public const string PlaybackItemNotFound = "playback_item_not_found";

    /// <summary>Problem code <c>playback_source_not_found</c>.</summary>
    public const string PlaybackSourceNotFound = "playback_source_not_found";

    /// <summary>Problem code <c>plugin_not_found</c>.</summary>
    public const string PluginNotFound = "plugin_not_found";

    /// <summary>Problem code <c>plugin_update_not_found</c>.</summary>
    public const string PluginUpdateNotFound = "plugin_update_not_found";

    /// <summary>Problem code <c>request_invalid</c>.</summary>
    public const string RequestInvalid = "request_invalid";

    /// <summary>Problem code <c>request_proposal_changed</c>.</summary>
    public const string RequestProposalChanged = "request_proposal_changed";

    /// <summary>Problem code <c>request_service_invalid</c>.</summary>
    public const string RequestServiceInvalid = "request_service_invalid";

    /// <summary>Problem code <c>root_not_found</c>.</summary>
    public const string RootNotFound = "root_not_found";

    /// <summary>Problem code <c>session_not_found</c>.</summary>
    public const string SessionNotFound = "session_not_found";

    /// <summary>Problem code <c>setting_invalid</c>.</summary>
    public const string SettingInvalid = "setting_invalid";

    /// <summary>Problem code <c>setting_not_found</c>.</summary>
    public const string SettingNotFound = "setting_not_found";

    /// <summary>Problem code <c>setup_already_completed</c>.</summary>
    public const string SetupAlreadyCompleted = "setup_already_completed";

    /// <summary>Problem code <c>unknown_job_type</c>.</summary>
    public const string UnknownJobType = "unknown_job_type";

    /// <summary>Problem code <c>user_invalid</c>.</summary>
    public const string UserInvalid = "user_invalid";

    /// <summary>Problem code <c>user_not_found</c>.</summary>
    public const string UserNotFound = "user_not_found";

    /// <summary>Problem code <c>unsupported_entity_image_role</c>.</summary>
    public const string UnsupportedEntityImageRole = "unsupported_entity_image_role";

    /// <summary>Problem code <c>video_hls_not_found</c>.</summary>
    public const string VideoHlsNotFound = "video_hls_not_found";

    /// <summary>Problem code <c>video_stream_not_found</c>.</summary>
    public const string VideoStreamNotFound = "video_stream_not_found";

    /// <summary>Problem code <c>video_subtitle_not_found</c>.</summary>
    public const string VideoSubtitleNotFound = "video_subtitle_not_found";

    /// <summary>Problem code <c>video_subtitle_source_not_found</c>.</summary>
    public const string VideoSubtitleSourceNotFound = "video_subtitle_source_not_found";

    /// <summary>Problem code <c>video_trickplay_not_found</c>.</summary>
    public const string VideoTrickplayNotFound = "video_trickplay_not_found";

    /// <summary>Problem code <c>video_trickplay_tile_not_found</c>.</summary>
    public const string VideoTrickplayTileNotFound = "video_trickplay_tile_not_found";
}
