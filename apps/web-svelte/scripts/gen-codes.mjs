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

// Backend enum type name -> [exported const name, exported type name] for families whose
// exported names predate the mechanical derivation below. Every OTHER manifest enum is
// exported automatically as SCREAMING_SNAKE(name) / `${name}Code`, so a new backend
// [Code] enum can never be silently unsurfaced.
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
  ["PluginSearchFieldType", "PLUGIN_SEARCH_FIELD_TYPE", "PluginSearchFieldTypeCode"],
  ["FileSourceKind", "FILE_SOURCE_KIND", "FileSourceKindCode"],
  ["FileEntryKind", "FILE_ENTRY_KIND", "FileEntryKindCode"],
  ["ThumbnailHoverKind", "THUMBNAIL_HOVER_KIND", "ThumbnailHoverKindCode"],
  ["ProgressUnit", "PROGRESS_UNIT", "ProgressUnitCode"],
  ["ReaderMode", "READER_MODE", "ReaderModeCode"],
  ["RequestProviderKind", "REQUEST_PROVIDER_KIND", "RequestProviderKindCode"],
  ["RequestMediaKind", "REQUEST_MEDIA_KIND", "RequestMediaKindCode"],
  ["RequestReviewSelection", "REQUEST_REVIEW_SELECTION", "RequestReviewSelectionCode"],
  ["LibraryRootMediaCapability", "LIBRARY_ROOT_MEDIA_CAPABILITY", "LibraryRootMediaCapabilityCode"],
  ["RequestCommitOutcome", "REQUEST_COMMIT_OUTCOME", "RequestCommitOutcomeCode"],
  ["IndexerKind", "INDEXER_KIND", "IndexerKindCode"],
  ["DownloadClientKind", "DOWNLOAD_CLIENT_KIND", "DownloadClientKindCode"],
  ["DownloadProtocol", "DOWNLOAD_PROTOCOL", "DownloadProtocolCode"],
  ["AcquisitionStatus", "ACQUISITION_STATUS", "AcquisitionStatusCode"],
  ["AcquisitionTeardownIntent", "ACQUISITION_TEARDOWN_INTENT", "AcquisitionTeardownIntentCode"],
  ["EntityLifecycleClaimKind", "ENTITY_LIFECYCLE_CLAIM_KIND", "EntityLifecycleClaimKindCode"],
  ["AcquisitionHistoryEvent", "ACQUISITION_HISTORY_EVENT", "AcquisitionHistoryEventCode"],
  ["ReleaseRejectionReason", "RELEASE_REJECTION_REASON", "ReleaseRejectionReasonCode"],
  ["CustomFormatConditionType", "CUSTOM_FORMAT_CONDITION_TYPE", "CustomFormatConditionTypeCode"],
  ["ImportMode", "IMPORT_MODE", "ImportModeCode"],
  ["BlocklistReason", "BLOCKLIST_REASON", "BlocklistReasonCode"],
  ["MonitorStatus", "MONITOR_STATUS", "MonitorStatusCode"],
  ["MonitorPreset", "MONITOR_PRESET", "MonitorPresetCode"],
  ["BookRendition", "BOOK_RENDITION", "BookRenditionCode"],
  ["BookFormatTier", "BOOK_FORMAT_TIER", "BookFormatTierCode"],
  ["BookSourceTier", "BOOK_SOURCE_TIER", "BookSourceTierCode"],
  ["ProperDownloadPolicy", "PROPER_DOWNLOAD_POLICY", "ProperDownloadPolicyCode"],
  ["UserRole", "USER_ROLE", "UserRoleCode"],
];

const screamingSnake = (name) =>
  name
    .replace(/([a-z0-9])([A-Z])/g, "$1_$2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1_$2")
    .toUpperCase();

const camel = (name) => {
  const joined = name.replace(/[^A-Za-z0-9_$]+([A-Za-z0-9_$])/g, (_, next) => next.toUpperCase());
  const key = joined.length === 0 ? joined : joined[0].toLowerCase() + joined.slice(1);
  return /^[A-Za-z_$]/.test(key) ? key : `_${key}`;
};
const lit = (value) => JSON.stringify(value);

// `source` names the backend registry the const is projected from. The annotation is
// machine-read by GeneratedCodesParityTests to verify the committed file offline.
function constBlock(constName, typeName, entries, source) {
  const keys = entries.map(([key]) => key);
  if (new Set(keys).size !== keys.length || keys.some((key) => !/^[A-Za-z_$][A-Za-z0-9_$]*$/.test(key))) {
    throw new Error(`${constName} contains duplicate or invalid generated property names`);
  }
  const body = entries.map(([key, value]) => `  ${key}: ${lit(value)},`).join("\n");
  return (
    `// source: ${source}\n` +
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

  const explicitNames = new Map(ENUM_EXPORTS.map(([enumName, constName, typeName]) => [enumName, [constName, typeName]]));
  const usedConstNames = new Set(ENUM_EXPORTS.map(([, constName]) => constName));
  for (const [enumName] of ENUM_EXPORTS) {
    if (!manifest.enums?.[enumName]) {
      throw new Error(`Manifest is missing enum '${enumName}'. Is the backend up to date?`);
    }
  }

  // Every manifest enum is exported: explicitly-named families first, then the rest with
  // mechanically derived names. Nothing in the backend code registry can stay unsurfaced.
  for (const enumName of Object.keys(manifest.enums ?? {}).sort()) {
    const [constName, typeName] = explicitNames.get(enumName) ?? [screamingSnake(enumName), `${enumName}Code`];
    if (!explicitNames.has(enumName)) {
      if (usedConstNames.has(constName)) {
        throw new Error(`Derived const name '${constName}' for enum '${enumName}' collides with an explicit export`);
      }
      usedConstNames.add(constName);
    }
    const members = manifest.enums[enumName];
    sections.push(constBlock(constName, typeName, members.map((m) => [camel(m.name), m.code]), `enum ${enumName}`));
  }

  // Capability discriminators (keyed by the code itself).
  sections.push(
    constBlock(
      "CAPABILITY_KIND",
      "CapabilityKindCode",
      (manifest.capabilityKinds ?? []).map((code) => [camel(code), code]),
      "registry CapabilityKinds",
    ),
  );

  // Well-known external-id providers.
  sections.push(
    constBlock(
      "EXTERNAL_ID_PROVIDER",
      "ExternalIdProviderCode",
      (manifest.externalIdProviders ?? []).map((c) => [camel(c.name), c.value]),
      "registry ExternalIdProviders",
    ),
  );

  // App setting keys (preserves the camelCase member -> dotted key shape consumers expect).
  sections.push(
    constBlock(
      "SETTING_KEYS",
      "SettingKey",
      (manifest.settingKeys ?? []).map((c) => [camel(c.name), c.value]),
      "registry AppSettingKeys",
    ),
  );

  // Machine-readable API problem codes (matched by clients instead of message text).
  sections.push(
    constBlock(
      "PROBLEM_CODE",
      "ProblemCode",
      (manifest.problemCodes ?? []).map((c) => [camel(c.name), c.value]),
      "registry ApiProblemCodes",
    ),
  );

  // Entity-kind plural display labels, keyed by kind code.
  const labelEntries = (manifest.entityKinds ?? []).map((k) => `  ${lit(k.code)}: ${lit(k.groupLabel)},`).join("\n");
  sections.push(
    `export const ENTITY_KIND_LABELS: Record<EntityKindCode, string> = {\n${labelEntries}\n};\n`,
  );

  // Complete frontend request-kind behavior, projected directly from RequestKindRegistry. Discover,
  // review, and target selectors consume this instead of maintaining a parallel handwritten table.
  if (!Array.isArray(manifest.requestKinds) || manifest.requestKinds.length === 0) {
    throw new Error("Manifest is missing RequestKindRegistry metadata. Is the backend up to date?");
  }
  const requestKindFields = [
    "kind", "label", "plural", "committable", "childNoun", "entityKind", "pluginEntityKind",
    "acquisitionKind", "profileKind", "rootFlag", "discoverable", "reviewSelection",
  ];
  for (const kind of manifest.requestKinds) {
    const missing = requestKindFields.filter((field) => !Object.hasOwn(kind, field));
    if (missing.length > 0) {
      throw new Error(`Request kind '${kind.kind ?? "unknown"}' is missing: ${missing.join(", ")}`);
    }
  }
  const requestKindEntries = manifest.requestKinds.map((kind) =>
    `  { kind: ${lit(kind.kind)}, label: ${lit(kind.label)}, plural: ${lit(kind.plural)}, ` +
    `committable: ${lit(kind.committable)}, childNoun: ${lit(kind.childNoun)}, ` +
    `entityKind: ${lit(kind.entityKind)}, pluginEntityKind: ${lit(kind.pluginEntityKind)}, ` +
    `acquisitionKind: ${lit(kind.acquisitionKind)}, ` +
    `profileKind: ${lit(kind.profileKind)}, rootFlag: ${lit(kind.rootFlag)}, ` +
    `discoverable: ${lit(kind.discoverable)}, reviewSelection: ${lit(kind.reviewSelection)} },`
  ).join("\n");
  sections.push(
    `export interface RequestKindManifestEntry {\n` +
      `  kind: RequestMediaKindCode;\n` +
      `  label: string;\n` +
      `  plural: string;\n` +
      `  committable: boolean;\n` +
      `  childNoun: string | null;\n` +
      `  entityKind: EntityKindCode;\n` +
      `  pluginEntityKind: EntityKindCode;\n` +
      `  acquisitionKind: EntityKindCode;\n` +
      `  profileKind: EntityKindCode | null;\n` +
      `  rootFlag: LibraryRootMediaCapabilityCode | null;\n` +
      `  discoverable: boolean;\n` +
      `  reviewSelection: RequestReviewSelectionCode;\n` +
      `}\n\n` +
      `export const REQUEST_KIND_MANIFEST = [\n${requestKindEntries}\n] as const satisfies readonly RequestKindManifestEntry[];\n`,
  );

  // Safe roots for the managed delete-files workflow. This list comes from EntityKindRegistry;
  // frontend browse/grid surfaces must not duplicate the backend kind policy by hand.
  const fileDeletionKinds = (manifest.entityKinds ?? [])
    .filter((kind) => kind.supportsFileDeletion === true)
    .map((kind) => `  ${lit(kind.code)},`)
    .join("\n");
  sections.push(
    `export const ENTITY_KINDS_SUPPORTING_FILE_DELETION = [\n${fileDeletionKinds}\n] as const;\n\n` +
      `export type FileDeletableEntityKindCode = (typeof ENTITY_KINDS_SUPPORTING_FILE_DELETION)[number];\n`,
  );

  // Entity kinds materialized by a committable RequestKindRegistry descriptor. Shared acquisition
  // surfaces consume this generated policy instead of maintaining a second frontend request registry.
  const requestableEntityKinds = (manifest.entityKinds ?? [])
    .filter((kind) => kind.supportsRequests === true)
    .map((kind) => `  ${lit(kind.code)},`)
    .join("\n");
  sections.push(
    `export const ENTITY_KINDS_SUPPORTING_REQUESTS = [\n${requestableEntityKinds}\n] as const;\n\n` +
      `export type RequestableEntityKindCode = (typeof ENTITY_KINDS_SUPPORTING_REQUESTS)[number];\n`,
  );

  // Identify containers whose local children are enumerated for cascade identify. The
  // identify review flow consumes this instead of hand-mirroring EntityKindRegistry flags.
  const identifyContainerKinds = (manifest.entityKinds ?? [])
    .filter((kind) => kind.enumeratesIdentifyChildren === true)
    .map((kind) => `  ${lit(kind.code)},`)
    .join("\n");
  sections.push(
    `export const ENTITY_KINDS_ENUMERATING_IDENTIFY_CHILDREN = [\n${identifyContainerKinds}\n] as const;\n\n` +
      `export type IdentifyContainerEntityKindCode = (typeof ENTITY_KINDS_ENUMERATING_IDENTIFY_CHILDREN)[number];\n`,
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
