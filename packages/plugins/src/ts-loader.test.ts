import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { loadTypeScriptPlugin } from "./ts-loader";
import type { PrismediaPluginManifest } from "./types";

describe("loadTypeScriptPlugin", () => {
  it("loads CommonJS plugin entries even when package metadata says module", async () => {
    const installDir = await mkdtemp(path.join(tmpdir(), "prismedia-plugin-"));
    try {
      await writeFile(
        path.join(installDir, "package.json"),
        JSON.stringify({ type: "module" }),
        "utf8",
      );
      await writeFile(
        path.join(installDir, "plugin.js"),
        `
          "use strict";
          Object.defineProperty(exports, "__esModule", { value: true });
          exports.default = {
            capabilities: { folderByName: true },
            async execute(action, input) {
              return { action, title: input.title };
            },
          };
        `,
        "utf8",
      );

      const manifest: PrismediaPluginManifest = {
        id: "cjs-plugin",
        name: "CJS Plugin",
        version: "1.0.0",
        runtime: "typescript",
        entry: "plugin.js",
        isNsfw: false,
        capabilities: { folderByName: true },
      };

      const plugin = await loadTypeScriptPlugin(manifest, installDir);
      const packageJson = JSON.parse(
        await readFile(path.join(installDir, "package.json"), "utf8"),
      ) as { type?: string };

      expect(packageJson.type).toBe("commonjs");
      await expect(
        plugin.execute("folderByName", { title: "Demo" }, {}),
      ).resolves.toEqual({ action: "folderByName", title: "Demo" });
    } finally {
      await rm(installDir, { recursive: true, force: true });
    }
  });
});
