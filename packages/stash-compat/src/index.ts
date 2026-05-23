// Types
export type {
  ScraperYamlDef,
  ScraperScriptDef,
  ScraperCapabilities,
  ScraperSceneFragment,
  ScraperPerformerFragment,
  ScraperSearchInput,
  StashScrapedScene,
  StashScrapedPerformer,
  StashScrapedStudio,
  StashScrapedTag,
  NormalizedScrapeResult,
} from "./types";
export { capabilityKeys } from "./types";

// YAML Parser
export {
  parseScraperYaml,
  resolveActionDef,
  ScraperParseError,
} from "./yaml-parser";

// XPath Scraper Engine
export { runXPathScraper } from "./xpath-scraper";

// Executor
export {
  runScraperScript,
  scrapeScene,
  scrapePerformer,
  ScraperExecutionError,
  type ExecutorOptions,
} from "./executor";

// Normalizer
export {
  normalizeSceneResult,
  normalizePerformerResult,
  hasUsableNormalizedSceneResult,
} from "./normalizer";

// StashBox Client
export {
  StashBoxClient,
  StashBoxError,
  normalizeStashBoxScene,
  normalizeStashBoxPerformer,
  stashBoxSceneToRawResult,
} from "./stashbox";
export type {
  StashBoxFingerprint,
  StashBoxScene,
  StashBoxPerformer,
  StashBoxStudio,
  StashBoxTag,
  FingerprintAlgorithm,
  FingerprintSubmissionInput,
} from "./stashbox";
