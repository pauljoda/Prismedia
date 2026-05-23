/**
 * Parse and validate Prismedia plugin manifest files (manifest.yml).
 */

import { readFile } from "node:fs/promises";
import path from "node:path";
import yaml from "js-yaml";
import type { PrismediaPluginManifest, PluginCapabilities } from "./types";
import { pluginCapabilityKeys } from "./types";

export class ManifestParseError extends Error {
  constructor(
    message: string,
    public readonly pluginPath: string,
  ) {
    super(message);
    this.name = "ManifestParseError";
  }
}

/**
 * Read and parse a manifest.yml file from the given plugin directory.
 */
export async function readManifest(
  pluginDir: string,
): Promise<PrismediaPluginManifest> {
  const manifestPath = path.join(pluginDir, "manifest.yml");
  let raw: string;
  try {
    raw = await readFile(manifestPath, "utf-8");
  } catch {
    throw new ManifestParseError(
      `Cannot read manifest.yml in ${pluginDir}`,
      pluginDir,
    );
  }

  const parsed = yaml.load(raw, { schema: yaml.JSON_SCHEMA });
  if (!parsed || typeof parsed !== "object") {
    throw new ManifestParseError(
      `Invalid YAML structure in manifest.yml`,
      pluginDir,
    );
  }

  return validateManifest(parsed as Record<string, unknown>, pluginDir);
}

/**
 * Validate a parsed manifest object against the PrismediaPluginManifest schema.
 */
export function validateManifest(
  raw: Record<string, unknown>,
  pluginDir: string,
): PrismediaPluginManifest {
  // Required fields
  const id = requireString(raw, "id", pluginDir);
  const name = requireString(raw, "name", pluginDir);
  const version = requireString(raw, "version", pluginDir);
  const runtime = requireString(raw, "runtime", pluginDir);

  if (!["python", "typescript", "stash-compat"].includes(runtime)) {
    throw new ManifestParseError(
      `Invalid runtime "${runtime}" — must be "python", "typescript", or "stash-compat"`,
      pluginDir,
    );
  }

  const isNsfw = typeof raw.isNsfw === "boolean" ? raw.isNsfw : false;

  // Capabilities
  const rawCaps = raw.capabilities;
  if (!rawCaps || typeof rawCaps !== "object") {
    throw new ManifestParseError(
      `Missing or invalid "capabilities" in manifest`,
      pluginDir,
    );
  }

  const capabilities: PluginCapabilities = {};
  for (const key of pluginCapabilityKeys) {
    const val = (rawCaps as Record<string, unknown>)[key];
    if (typeof val === "boolean") {
      capabilities[key] = val;
    }
  }

  // Auth fields
  let auth: PrismediaPluginManifest["auth"];
  if (Array.isArray(raw.auth)) {
    auth = raw.auth.map((entry: unknown) => {
      if (!entry || typeof entry !== "object") {
        throw new ManifestParseError(
          `Invalid auth entry in manifest`,
          pluginDir,
        );
      }
      const e = entry as Record<string, unknown>;
      return {
        key: String(e.key ?? ""),
        label: String(e.label ?? e.key ?? ""),
        required: e.required !== false,
        url: typeof e.url === "string" ? e.url : undefined,
      };
    });
  }

  return {
    id,
    name,
    version,
    author: optionalString(raw, "author"),
    description: optionalString(raw, "description"),
    homepage: optionalString(raw, "homepage"),
    isNsfw,
    tags: Array.isArray(raw.tags)
      ? raw.tags.filter((t): t is string => typeof t === "string")
      : undefined,
    runtime: runtime as PrismediaPluginManifest["runtime"],
    entry: optionalString(raw, "entry"),
    script: Array.isArray(raw.script)
      ? raw.script.map(String)
      : undefined,
    stashDefinition: optionalString(raw, "stashDefinition"),
    requires: Array.isArray(raw.requires)
      ? raw.requires.map(String)
      : undefined,
    auth,
    capabilities,
  };
}

function requireString(
  raw: Record<string, unknown>,
  key: string,
  pluginDir: string,
): string {
  const val = raw[key];
  if (typeof val !== "string" || !val.trim()) {
    throw new ManifestParseError(
      `Missing or empty required field "${key}" in manifest`,
      pluginDir,
    );
  }
  return val.trim();
}

function optionalString(
  raw: Record<string, unknown>,
  key: string,
): string | undefined {
  const val = raw[key];
  return typeof val === "string" && val.trim() ? val.trim() : undefined;
}
