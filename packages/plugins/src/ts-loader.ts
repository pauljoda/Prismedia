/**
 * TypeScript plugin loader — dynamically loads compiled JS plugins.
 *
 * TypeScript plugins are compiled to CommonJS at install time. The loader
 * validates the entry path is within the plugin directory (path traversal guard)
 * and duck-type validates the exported module against the PrismediaPlugin interface.
 */

import path from "node:path";
import { pathToFileURL } from "node:url";
import { createRequire } from "node:module";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import type { PrismediaPlugin, PrismediaPluginManifest } from "./types";
import { PluginExecutionError } from "./executor";

/**
 * Plugins ship compiled JS. Most today are bundled as CommonJS (the
 * entry file contains `exports.default = {...}`). Node resolves the
 * module format by walking up for the nearest `package.json`. In dev and
 * production plugin cache directories, that nearest package can be ESM, so
 * Node tries to parse CJS code as ESM and fails with
 * "exports is not defined in ES module scope".
 *
 * Detect CJS entries before loading them. Node can load these directly
 * through `require`, which avoids dev-server ESM transforms that ignore
 * package metadata for runtime plugin files. We also write/update a local
 * sentinel `package.json` so worker and production Node entrypoints resolve
 * the file consistently.
 */
function ensureCjsSentinel(entryPath: string): boolean {
  try {
    const contents = readFileSync(entryPath, "utf8");
    const looksLikeCjs =
      /\bexports\.[a-zA-Z_$]/.test(contents) ||
      /\bmodule\.exports\b/.test(contents) ||
      /"use strict";/.test(contents);
    if (!looksLikeCjs) return false;
    const sentinelPath = path.join(path.dirname(entryPath), "package.json");
    const existing = existsSync(sentinelPath)
      ? (JSON.parse(readFileSync(sentinelPath, "utf8")) as Record<string, unknown>)
      : {};
    if (existing.type === "commonjs") return true;
    writeFileSync(
      sentinelPath,
      JSON.stringify({ ...existing, type: "commonjs" }, null, 2) + "\n",
      "utf8",
    );
    return true;
  } catch {
    // Best-effort — if detection or writing fails, fall through and let
    // the loader below throw a meaningful error.
    return false;
  }
}

const requirePlugin = createRequire(import.meta.url);

/**
 * Load a TypeScript plugin's compiled JS entry point and return it
 * as a PrismediaPlugin interface.
 */
export async function loadTypeScriptPlugin(
  manifest: PrismediaPluginManifest,
  installDir: string,
): Promise<PrismediaPlugin> {
  if (!manifest.entry) {
    throw new PluginExecutionError(
      `Plugin "${manifest.id}" has no entry point defined`,
      manifest.id,
      "load",
    );
  }

  const entryPath = path.resolve(installDir, manifest.entry);

  // Path traversal guard
  if (!entryPath.startsWith(path.resolve(installDir))) {
    throw new PluginExecutionError(
      `Plugin entry path escapes install directory: ${manifest.entry}`,
      manifest.id,
      "load",
    );
  }

  if (!existsSync(entryPath)) {
    throw new PluginExecutionError(
      `Plugin entry file not found: ${entryPath}`,
      manifest.id,
      "load",
    );
  }

  // Ensure CJS bundles load correctly regardless of the nearest
  // package.json's "type" setting in the hosting app.
  const isCommonJsEntry = ensureCjsSentinel(entryPath);

  // Dynamic import — works for both ESM and CJS (Node resolves). The
  // plugin entry path is only known at runtime, so keep Vite from
  // trying to statically analyze it when this server-only loader is
  // pulled into the SvelteKit graph.
  let mod: Record<string, unknown>;
  try {
    if (isCommonJsEntry) {
      const resolvedEntry = requirePlugin.resolve(entryPath);
      delete requirePlugin.cache[resolvedEntry];
      mod = requirePlugin(resolvedEntry) as Record<string, unknown>;
    } else {
      mod = await import(/* @vite-ignore */ pathToFileURL(entryPath).href);
    }
  } catch (err) {
    throw new PluginExecutionError(
      `Failed to load plugin: ${err instanceof Error ? err.message : String(err)}`,
      manifest.id,
      "load",
    );
  }

  // The module should export a default that conforms to PrismediaPlugin
  const plugin = (mod.default ?? mod) as Record<string, unknown>;

  // Duck-type validation
  if (typeof plugin.execute !== "function") {
    throw new PluginExecutionError(
      `Plugin "${manifest.id}" does not export an execute() function`,
      manifest.id,
      "load",
    );
  }

  if (!plugin.capabilities || typeof plugin.capabilities !== "object") {
    throw new PluginExecutionError(
      `Plugin "${manifest.id}" does not export a capabilities object`,
      manifest.id,
      "load",
    );
  }

  return plugin as unknown as PrismediaPlugin;
}
