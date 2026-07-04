// Generates src/lib/api/generated/codes.ts from the backend code-registry manifest.
//
// The backend is the single source of truth for stable codes ([Code] enums, capability
// discriminators, external-id provider keys, and setting keys). This script fetches the
// dev-only manifest endpoint and emits matching TypeScript constants so the frontend never
// hand-maintains these values. Run as part of `pnpm api:generate` (after orval), against a
// running dev API.

import { writeFileSync, mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));
const OUTPUT = resolve(__dirname, "../src/lib/api/generated/codes.ts");

const openApiUrl = process.env.PRISMEDIA_OPENAPI_URL ?? "http://127.0.0.1:8008/openapi/v1.json";
const codesUrl = process.env.PRISMEDIA_CODES_URL ?? new URL("/api/_codegen/codes.json", openApiUrl).toString();

// Backend enum type name -> [exported const name, exported type name].
const ENUM_EXPORTS = [
  ["EntityKind", "ENTITY_KIND", "EntityKindCode"],
  ["ProposalKind", "PROPOSAL_KIND", "ProposalKindCode"],
  ["RelationshipKind", "RELATIONSHIP_CODE", "RelationshipCode"],
  ["EntityFileRole", "ENTITY_FILE_ROLE", "EntityFileRoleCode"],
  ["CreditRole", "CREDIT_ROLE", "CreditRoleCode"],
  ["JobType", "JOB_TYPE", "JobTypeCode"],
  ["VideoQuality", "VIDEO_QUALITY", "VideoQualityCode"],
  ["AudioQuality", "AUDIO_QUALITY", "AudioQualityCode"],
  ["JobRunStatus", "JOB_RUN_STATUS", "JobRunStatusCode"],
  ["DatabaseBackupStatus", "DATABASE_BACKUP_STATUS", "DatabaseBackupStatusCode"],
  ["PlaybackMode", "PLAYBACK_MODE", "PlaybackModeCode"],
  ["PlaybackEventKind", "PLAYBACK_EVENT_KIND", "PlaybackEventKindCode"],
  ["MusicPlayerRepeatMode", "MUSIC_PLAYER_REPEAT_MODE", "MusicPlayerRepeatModeCode"],
  ["MusicPlayerMiniSide", "MUSIC_PLAYER_MINI_SIDE", "MusicPlayerMiniSideCode"],
  ["EntitySubtitleSource", "SUBTITLE_SOURCE", "SubtitleSourceCode"],
  ["SubtitleStyle", "SUBTITLE_STYLE", "SubtitleStyleCode"],
  ["IdentifyAction", "IDENTIFY_ACTION", "IdentifyActionCode"],
  ["IdentifyQueueState", "IDENTIFY_QUEUE_STATE", "IdentifyQueueStateCode"],
  ["IdentifyResultStatus", "IDENTIFY_RESULT_STATUS", "IdentifyResultStatusCode"],
  ["IdentifyApplyState", "IDENTIFY_APPLY_STATE", "IdentifyApplyStateCode"],
  ["FileSourceKind", "FILE_SOURCE_KIND", "FileSourceKindCode"],
  ["FileEntryKind", "FILE_ENTRY_KIND", "FileEntryKindCode"],
  ["ThumbnailHoverKind", "THUMBNAIL_HOVER_KIND", "ThumbnailHoverKindCode"],
  ["ProgressUnit", "PROGRESS_UNIT", "ProgressUnitCode"],
  ["ReaderMode", "READER_MODE", "ReaderModeCode"],
  ["RequestProviderKind", "REQUEST_PROVIDER_KIND", "RequestProviderKindCode"],
  ["RequestMediaKind", "REQUEST_MEDIA_KIND", "RequestMediaKindCode"],
  ["RequestRatingSource", "REQUEST_RATING_SOURCE", "RequestRatingSourceCode"],
  ["RequestCommitOutcome", "REQUEST_COMMIT_OUTCOME", "RequestCommitOutcomeCode"],
  ["IndexerKind", "INDEXER_KIND", "IndexerKindCode"],
  ["DownloadClientKind", "DOWNLOAD_CLIENT_KIND", "DownloadClientKindCode"],
  ["DownloadProtocol", "DOWNLOAD_PROTOCOL", "DownloadProtocolCode"],
  ["AcquisitionStatus", "ACQUISITION_STATUS", "AcquisitionStatusCode"],
  ["AcquisitionHistoryEvent", "ACQUISITION_HISTORY_EVENT", "AcquisitionHistoryEventCode"],
  ["ReleaseRejectionReason", "RELEASE_REJECTION_REASON", "ReleaseRejectionReasonCode"],
  ["CustomFormatConditionType", "CUSTOM_FORMAT_CONDITION_TYPE", "CustomFormatConditionTypeCode"],
  ["ImportMode", "IMPORT_MODE", "ImportModeCode"],
  ["BlocklistReason", "BLOCKLIST_REASON", "BlocklistReasonCode"],
  ["MonitorStatus", "MONITOR_STATUS", "MonitorStatusCode"],
  ["MonitorPreset", "MONITOR_PRESET", "MonitorPresetCode"],
  ["BookFormatTier", "BOOK_FORMAT_TIER", "BookFormatTierCode"],
  ["BookSourceTier", "BOOK_SOURCE_TIER", "BookSourceTierCode"],
  ["ProperDownloadPolicy", "PROPER_DOWNLOAD_POLICY", "ProperDownloadPolicyCode"],
];

const camel = (name) => (name.length === 0 ? name : name[0].toLowerCase() + name.slice(1));
const lit = (value) => JSON.stringify(value);

function constBlock(constName, typeName, entries) {
  const body = entries.map(([key, value]) => `  ${key}: ${lit(value)},`).join("\n");
  return (
    `export const ${constName} = {\n${body}\n} as const;\n\n` +
    `export type ${typeName} = (typeof ${constName})[keyof typeof ${constName}];\n`
  );
}

async function main() {
  const response = await fetch(codesUrl);
  if (!response.ok) {
    throw new Error(`Failed to fetch codes manifest from ${codesUrl}: ${response.status} ${response.statusText}`);
  }
  const manifest = await response.json();

  const sections = [];

  for (const [enumName, constName, typeName] of ENUM_EXPORTS) {
    const members = manifest.enums?.[enumName];
    if (!members) {
      throw new Error(`Manifest is missing enum '${enumName}'. Is the backend up to date?`);
    }
    sections.push(constBlock(constName, typeName, members.map((m) => [camel(m.name), m.code])));
  }

  // Capability discriminators (keyed by the code itself).
  sections.push(
    constBlock(
      "CAPABILITY_KIND",
      "CapabilityKindCode",
      (manifest.capabilityKinds ?? []).map((code) => [camel(code), code]),
    ),
  );

  // Well-known external-id providers.
  sections.push(
    constBlock(
      "EXTERNAL_ID_PROVIDER",
      "ExternalIdProviderCode",
      (manifest.externalIdProviders ?? []).map((c) => [camel(c.name), c.value]),
    ),
  );

  // App setting keys (preserves the camelCase member -> dotted key shape consumers expect).
  sections.push(
    constBlock(
      "SETTING_KEYS",
      "SettingKey",
      (manifest.settingKeys ?? []).map((c) => [camel(c.name), c.value]),
    ),
  );

  // Entity-kind plural display labels, keyed by kind code.
  const labelEntries = (manifest.entityKinds ?? []).map((k) => `  ${lit(k.code)}: ${lit(k.groupLabel)},`).join("\n");
  sections.push(
    `export const ENTITY_KIND_LABELS: Record<EntityKindCode, string> = {\n${labelEntries}\n};\n`,
  );

  const header =
    "// AUTO-GENERATED by scripts/gen-codes.mjs from the backend code-registry manifest.\n" +
    "// Do not edit by hand. Run `pnpm api:generate` (with the dev API running) to refresh.\n" +
    "/* eslint-disable */\n";

  mkdirSync(dirname(OUTPUT), { recursive: true });
  writeFileSync(OUTPUT, `${header}\n${sections.join("\n")}`);
  console.log(`Wrote ${OUTPUT}`);
}

main().catch((error) => {
  console.error(error.message ?? error);
  process.exit(1);
});
