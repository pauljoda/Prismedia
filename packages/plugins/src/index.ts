// Types
export type {
  PrismediaPluginManifest,
  PluginRuntime,
  PluginAuthField,
  PluginCapabilities,
  PluginInput,
  PluginExecutionInput,
  PluginExecutionOutput,
  BatchItem,
  PrismediaPlugin,
  NormalizedVideoResult,
  NormalizedSeriesRef,
  NormalizedFolderResult,
  EpisodeMapping,
  NormalizedGalleryResult,
  NormalizedGalleryCandidate,
  NormalizedBookResult,
  NormalizedBookCandidate,
  NormalizedImageResult,
  NormalizedAudioTrackResult,
  NormalizedAudioLibraryResult,
  PluginResult,
  SeriesCandidate,
  InstalledPluginDto,
  PluginIndexEntry,
} from "./types";
export {
  pluginCapabilityKeys,
  prismediaToStashActionMap,
} from "./types";

// Manifest Parser
export {
  readManifest,
  validateManifest,
  ManifestParseError,
} from "./manifest-parser";

// Auth
export {
  encryptAuthValue,
  decryptAuthValue,
  resolvePluginAuth,
} from "./auth";

// Executor
export {
  runNativePythonPlugin,
  runNativePythonPluginBatch,
  PluginExecutionError,
  type PluginExecutorOptions,
} from "./executor";

// TypeScript Loader
export { loadTypeScriptPlugin } from "./ts-loader";

// Normalizers
export {
  normalizeVideoResult,
  hasUsableVideoResult,
  normalizeFolderResult,
  normalizeGalleryResult,
  normalizeBookResult,
  normalizeImageResult,
  normalizeAudioTrackResult,
  normalizeAudioLibraryResult,
} from "./normalizer";

// Batch
export {
  parseEpisodeFromFilename,
  matchScenesToEpisodes,
  fanOut,
  type SceneFileInfo,
  type EpisodeMatch,
} from "./batch";

// Index Fetcher
export {
  fetchPluginIndex,
  clearPluginIndexCache,
  resolveIndexUrl,
  resolveEntryZipUrl,
} from "./index-fetcher";

export * from "./external-ids";

export * from "./normalized-video";
