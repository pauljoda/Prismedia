// Prepares JASSUB runtime assets under apps/web-svelte/static/jassub so
// SvelteKit serves them as static files at /jassub/*. The <AssSubtitleOverlay>
// component hands the worker URL + wasm URL to JASSUB; the component and
// JASSUB itself are dynamically imported, so nothing here needs to be in
// the web app bundle.
//
// What ends up in static/jassub:
//   - jassub-client.js   — bundled browser entry with dependency interop
//                          resolved ahead of time so Vite never has to
//                          process the upstream package directly.
//   - jassub-worker.js   — bundled Web Worker entry (built from
//                          jassub/dist/worker/worker.js with esbuild).
//   - jassub-worker.wasm         — baseline libass build
//   - jassub-worker-modern.wasm  — SIMD-enabled build
//   - default.woff2              — fallback font

import { mkdir, copyFile } from "node:fs/promises";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";
import * as esbuild from "esbuild";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const require = createRequire(import.meta.url);

const pkgJsonPath = require.resolve("jassub/package.json");
const distDir = path.join(path.dirname(pkgJsonPath), "dist");

const outDir = path.join(__dirname, "..", "static", "jassub");
await mkdir(outDir, { recursive: true });

await esbuild.build({
  entryPoints: [path.join(distDir, "jassub.js")],
  outfile: path.join(outDir, "jassub-client.js"),
  bundle: true,
  format: "esm",
  platform: "browser",
  target: "es2022",
  minify: true,
  sourcemap: false,
  loader: {
    ".wasm": "copy",
    ".woff2": "copy",
  },
});

await esbuild.build({
  entryPoints: [path.join(distDir, "worker", "worker.js")],
  outfile: path.join(outDir, "jassub-worker.js"),
  bundle: true,
  format: "esm",
  platform: "browser",
  target: "es2022",
  minify: true,
  sourcemap: false,
  loader: { ".wasm": "empty" },
});

const staticFiles = [
  ["wasm/jassub-worker.wasm", "jassub-worker.wasm"],
  ["wasm/jassub-worker-modern.wasm", "jassub-worker-modern.wasm"],
  ["default.woff2", "default.woff2"],
];

for (const [rel, out] of staticFiles) {
  const src = path.join(distDir, rel);
  const dest = path.join(outDir, out);
  try {
    await copyFile(src, dest);
  } catch (err) {
    console.error(`[copy-jassub-assets] failed ${rel}:`, err.message);
    process.exit(1);
  }
}

console.log(
  `[copy-jassub-assets] bundled client/worker + copied ${staticFiles.length} static files -> ${outDir}`,
);
