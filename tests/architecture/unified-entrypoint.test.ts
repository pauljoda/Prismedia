import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const repositoryRoot = fileURLToPath(new URL("../../", import.meta.url));
const entrypoint = readFileSync(
  new URL("infra/docker/entrypoint.sh", `file://${repositoryRoot}`),
  "utf8",
);

describe("unified container entrypoint", () => {
  it.each([0, 23, "signal"])("restarts a worker after exit %s with fail-fast startup enabled", (failure) => {
    const workerStart = entrypoint.indexOf("Starting background worker");
    const loopStart = entrypoint.indexOf("  while true; do", workerStart);
    const loopEnd = entrypoint.indexOf("\n) &", loopStart);
    expect(loopStart).toBeGreaterThan(workerStart);
    expect(loopEnd).toBeGreaterThan(loopStart);
    // Execute the actual supervisor loop in a real shell. Only the worker and restart delay are
    // substituted: the second launch exits the test shell, so a broken supervisor cannot hang CI.
    const result = spawnSync("sh", ["-c", `
set -e
launch_count=0
dotnet() {
  launch_count=$((launch_count + 1))
  printf 'launch-%s\\n' "$launch_count"
  if [ "$launch_count" -gt 1 ]; then exit 0; fi
  ${failure === "signal" ? `sh -c 'kill -KILL "$$"'\n  return $?` : `return ${failure}`}
}
sleep() { :; }
${entrypoint.slice(loopStart, loopEnd)}
`], { encoding: "utf8", timeout: 2_000 });

    expect(result.stdout).toContain("launch-2");
    expect(result.stdout).toContain(`Worker exited (code ${failure === "signal" ? 137 : failure})`);
    expect(result.status).toBe(0);
  });

  it("enforces a PostgreSQL-compatible data-directory mode immediately before startup", () => {
    const ownershipIndex = entrypoint.indexOf(
      'chown -R postgres:postgres "$PGDATA" /run/postgresql',
    );
    const permissionIndex = entrypoint.indexOf('chmod 0750 "$PGDATA"');
    const postgresStartIndex = entrypoint.indexOf(
      'gosu postgres pg_ctl -D "$PGDATA"',
    );

    expect(ownershipIndex).toBeGreaterThanOrEqual(0);
    expect(permissionIndex).toBeGreaterThan(ownershipIndex);
    expect(postgresStartIndex).toBeGreaterThan(permissionIndex);
  });
});
