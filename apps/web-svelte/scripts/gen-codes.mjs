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

  const usedConstNames = new Set();
  const usedTypeNames = new Set();
  // Every manifest enum carries its generated symbols from the backend. A new code family cannot
  // be silently omitted, and exceptional public names live as annotations on their defining type.
  for (const enumName of Object.keys(manifest.enums ?? {}).sort()) {
    const family = manifest.codeFamilies?.[enumName];
    if (!family) {
      throw new Error(`Manifest enum '${enumName}' is missing its code-family names`);
    }
    const { constantName: constName, typeName } = family;
    if (!/^[A-Z][A-Z0-9_]*$/.test(constName) || !/^[A-Za-z_$][A-Za-z0-9_$]*$/.test(typeName)) {
      throw new Error(`Manifest enum '${enumName}' has invalid generated symbols '${constName}'/'${typeName}'`);
    }
    if (usedConstNames.has(constName) || usedTypeNames.has(typeName)) {
      throw new Error(`Manifest enum '${enumName}' duplicates generated symbols '${constName}'/'${typeName}'`);
    }
    usedConstNames.add(constName);
    usedTypeNames.add(typeName);
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

  // Compact thumbnail metadata icon vocabulary.
  sections.push(
    constBlock(
      "THUMBNAIL_META_ICON",
      "ThumbnailMetaIconCode",
      (manifest.thumbnailMetaIcons ?? []).map((c) => [camel(c.name), c.value]),
      "registry EntityThumbnailMetaIcons",
    ),
  );

  // Complete Entity-kind definitions. One generated object carries identity, labels, storage facts,
  // presentation, and behavior flags so clients do not rebuild parallel maps for each concern.
  const entityKindFields = [
    "code", "displayName", "groupLabel", "category", "storageShape", "icon", "referenceIcon",
    "thumbnailWidth", "thumbnailHeight", "primaryAccent", "secondaryAccent", "artworkFit",
    "navigation", "search", "autoIdentifySelector", "identifyPluginFallbackKind", "containableKinds", "mediaQualityFamily",
    "supportsFileDeletion", "supportsAtomicMediaUpgrade", "supportsManualManagement",
    "manualAcquisition",
    "engagementMode", "aggregatesDirectChildPlayback",
    "supportsRequests", "enumeratesIdentifyChildren", "acquisitionProfile",
  ];
  for (const kind of manifest.entityKinds ?? []) {
    const missing = entityKindFields.filter((field) => !Object.hasOwn(kind, field));
    if (missing.length > 0) {
      throw new Error(`Entity kind '${kind.code ?? "unknown"}' definition is missing: ${missing.join(", ")}`);
    }
    if (kind.navigation !== null) {
      const missingNavigation = [
        "canonicalBrowseKind", "destinationId", "browsePath", "detailPathTemplate",
        "requiredAncestorKind", "isTopLevel",
      ].filter((field) => !Object.hasOwn(kind.navigation, field));
      if (missingNavigation.length > 0) {
        throw new Error(`Entity kind '${kind.code}' navigation is missing: ${missingNavigation.join(", ")}`);
      }
    }
    if (kind.search !== null) {
      const missingSearch = ["order", "expandsRelationshipResults"]
        .filter((field) => !Object.hasOwn(kind.search, field));
      if (missingSearch.length > 0) {
        throw new Error(`Entity kind '${kind.code}' search is missing: ${missingSearch.join(", ")}`);
      }
    }
    const missingManualAcquisition = ["supportsUpload", "supportsReplacement"]
      .filter((field) => !Object.hasOwn(kind.manualAcquisition, field));
    if (missingManualAcquisition.length > 0) {
      throw new Error(`Entity kind '${kind.code}' manual acquisition is missing: ${missingManualAcquisition.join(", ")}`);
    }
    if (kind.acquisitionProfile !== null) {
      const missingProfile = [
        "label", "displayOrder", "libraryRootMediaCapability", "supportedReleaseDateTypes",
        "defaultNamingTemplate", "namingHint", "namingFamily",
      ].filter((field) => !Object.hasOwn(kind.acquisitionProfile, field));
      if (missingProfile.length > 0) {
        throw new Error(`Entity kind '${kind.code}' acquisition profile is missing: ${missingProfile.join(", ")}`);
      }
    }
  }
  const entityKindEntries = (manifest.entityKinds ?? []).map((kind) =>
    `  ${lit(kind.code)}: { kind: ${lit(kind.code)}, displayName: ${lit(kind.displayName)}, ` +
    `groupLabel: ${lit(kind.groupLabel)}, category: ${lit(kind.category)}, ` +
    `storageShape: ${lit(kind.storageShape)}, presentation: { icon: ${lit(kind.icon)}, ` +
    `referenceIcon: ${lit(kind.referenceIcon)}, thumbnailWidth: ${lit(kind.thumbnailWidth)}, ` +
    `thumbnailHeight: ${lit(kind.thumbnailHeight)}, primaryAccent: ${lit(kind.primaryAccent)}, ` +
    `secondaryAccent: ${lit(kind.secondaryAccent)}, artworkFit: ${lit(kind.artworkFit)} }, ` +
    `navigation: ${lit(kind.navigation)}, search: ${lit(kind.search)}, ` +
    `autoIdentifySelector: ${lit(kind.autoIdentifySelector)}, ` +
    `identifyPluginFallbackKind: ${lit(kind.identifyPluginFallbackKind)}, ` +
    `containableKinds: ${lit(kind.containableKinds)}, ` +
    `mediaQualityFamily: ${lit(kind.mediaQualityFamily)}, supportsFileDeletion: ${lit(kind.supportsFileDeletion)}, ` +
    `supportsAtomicMediaUpgrade: ${lit(kind.supportsAtomicMediaUpgrade)}, ` +
    `supportsManualManagement: ${lit(kind.supportsManualManagement)}, ` +
    `manualAcquisition: ${lit(kind.manualAcquisition)}, ` +
    `engagementMode: ${lit(kind.engagementMode)}, ` +
    `aggregatesDirectChildPlayback: ${lit(kind.aggregatesDirectChildPlayback)}, ` +
    `supportsRequests: ${lit(kind.supportsRequests)}, ` +
    `enumeratesIdentifyChildren: ${lit(kind.enumeratesIdentifyChildren)}, ` +
    `acquisitionProfile: ${lit(kind.acquisitionProfile)} },`
  ).join("\n");
  const searchableKinds = (manifest.entityKinds ?? [])
    .filter((kind) => kind.search !== null)
    .sort((left, right) => left.search.order - right.search.order);
  const searchKindEntries = searchableKinds.map((kind) => `  ${lit(kind.code)},`).join("\n");
  const relationshipSearchKindEntries = searchableKinds
    .filter((kind) => kind.search.expandsRelationshipResults)
    .map((kind) => `  ${lit(kind.code)},`)
    .join("\n");
  const entityKindCategories = [...new Set((manifest.entityKinds ?? []).map((kind) => kind.category))]
    .sort((left, right) => left.localeCompare(right));
  sections.push(
    constBlock(
      "ENTITY_KIND_CATEGORY",
      "EntityKindCategoryCode",
      entityKindCategories.map((category) => [camel(category), category]),
      "registry EntityKindDefinitions.Category",
    ),
  );
  sections.push(
    `export interface EntityKindPresentationManifestEntry {\n` +
      `  icon: EntityKindIconCode;\n` +
      `  referenceIcon: EntityKindIconCode;\n` +
      `  thumbnailWidth: number;\n` +
      `  thumbnailHeight: number;\n` +
      `  primaryAccent: EntityAccentHueCode;\n` +
      `  secondaryAccent: EntityAccentHueCode;\n` +
      `  artworkFit: EntityArtworkFitCode;\n` +
      `}\n\n` +
      `export interface EntityKindNavigationManifestEntry {\n` +
      `  canonicalBrowseKind: EntityKindCode;\n` +
      `  destinationId: string;\n` +
      `  browsePath: string;\n` +
      `  detailPathTemplate: string | null;\n` +
      `  requiredAncestorKind: EntityKindCode | null;\n` +
      `  isTopLevel: boolean;\n` +
      `}\n\n` +
      `export interface EntityKindSearchManifestEntry {\n` +
      `  order: number;\n` +
      `  expandsRelationshipResults: boolean;\n` +
      `}\n\n` +
      `export interface EntityManualAcquisitionManifestEntry {\n` +
      `  supportsUpload: boolean;\n` +
      `  supportsReplacement: boolean;\n` +
      `}\n\n` +
      `export interface EntityKindDefinitionManifestEntry {\n` +
      `  kind: EntityKindCode;\n` +
      `  displayName: string;\n` +
      `  groupLabel: string;\n` +
      `  category: EntityKindCategoryCode;\n` +
      `  storageShape: string;\n` +
      `  presentation: EntityKindPresentationManifestEntry;\n` +
      `  navigation: EntityKindNavigationManifestEntry | null;\n` +
      `  search: EntityKindSearchManifestEntry | null;\n` +
      `  autoIdentifySelector: AutoIdentifySelectorKindCode | null;\n` +
      `  identifyPluginFallbackKind: EntityKindCode | null;\n` +
      `  containableKinds: readonly EntityKindCode[] | null;\n` +
      `  mediaQualityFamily: EntityMediaQualityFamilyCode;\n` +
      `  supportsFileDeletion: boolean;\n` +
      `  supportsAtomicMediaUpgrade: boolean;\n` +
      `  supportsManualManagement: boolean;\n` +
      `  manualAcquisition: EntityManualAcquisitionManifestEntry;\n` +
      `  engagementMode: EntityEngagementModeCode;\n` +
      `  aggregatesDirectChildPlayback: boolean;\n` +
      `  supportsRequests: boolean;\n` +
      `  enumeratesIdentifyChildren: boolean;\n` +
      `  acquisitionProfile: AcquisitionProfileManifestEntry | null;\n` +
      `}\n\n` +
      `export const ENTITY_KIND_DEFINITIONS = {\n` +
      `${entityKindEntries}\n} as const satisfies Record<EntityKindCode, EntityKindDefinitionManifestEntry>;\n\n` +
      `export const ENTITY_KINDS_IN_GLOBAL_SEARCH = [\n${searchKindEntries}\n] as const;\n\n` +
      `export type GlobalSearchEntityKindCode = (typeof ENTITY_KINDS_IN_GLOBAL_SEARCH)[number];\n\n` +
      `export const ENTITY_KINDS_EXPANDING_RELATED_SEARCH_RESULTS = [\n` +
      `${relationshipSearchKindEntries}\n] as const satisfies readonly GlobalSearchEntityKindCode[];\n`,
  );
  sections.push(
    `export interface AcquisitionProfileManifestEntry {\n` +
      `  label: string;\n` +
      `  displayOrder: number;\n` +
      `  libraryRootMediaCapability: LibraryRootMediaCapabilityCode;\n` +
      `  supportedReleaseDateTypes: readonly EntityDateTypeCode[];\n` +
      `  defaultNamingTemplate: string;\n` +
      `  namingHint: string;\n` +
      `  namingFamily: AcquisitionNamingFamilyCode;\n` +
      `}\n`,
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

  // Entity kinds users may directly create and delete. The endpoint adapters remain client-local,
  // while eligibility comes from each canonical Entity-kind definition.
  const manuallyManageableKinds = (manifest.entityKinds ?? [])
    .filter((kind) => kind.supportsManualManagement === true)
    .map((kind) => `  ${lit(kind.code)},`)
    .join("\n");
  sections.push(
    `export const ENTITY_KINDS_SUPPORTING_MANUAL_MANAGEMENT = [\n${manuallyManageableKinds}\n] as const;\n\n` +
      `export type ManuallyManageableEntityKindCode = (typeof ENTITY_KINDS_SUPPORTING_MANUAL_MANAGEMENT)[number];\n`,
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
