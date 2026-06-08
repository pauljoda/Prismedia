// CI/dev parity guardrail: regenerates the API client + codes.ts against the running dev
// API and fails if the committed output is stale. A backend contract or [Code] enum change
// that isn't regenerated (the silent-drift failure mode the audit found) breaks the build.
//
// Requires the dev API to be reachable (PRISMEDIA_OPENAPI_URL, default http://127.0.0.1:8008).
import { execSync } from "node:child_process";

const generatedPath = "src/lib/api/generated";

function run(cmd) {
  execSync(cmd, { stdio: "inherit" });
}

console.log("Regenerating API client + codes…");
run("orval --config orval.config.ts");
run("node scripts/gen-codes.mjs");

try {
  execSync(`git diff --exit-code -- ${generatedPath}`, { stdio: "inherit" });
  console.log("✓ Generated client is in sync with the backend.");
} catch {
  console.error(
    "\n✗ Generated API client is OUT OF DATE.\n" +
      "  A backend contract/enum changed without regenerating the frontend client.\n" +
      "  Run `pnpm api:generate` (with the dev API up) and commit src/lib/api/generated.\n",
  );
  process.exit(1);
}
