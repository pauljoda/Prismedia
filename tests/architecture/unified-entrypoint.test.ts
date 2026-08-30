import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const repositoryRoot = fileURLToPath(new URL("../../", import.meta.url));
const entrypoint = readFileSync(
  new URL("infra/docker/entrypoint.sh", `file://${repositoryRoot}`),
  "utf8",
);

describe("unified container entrypoint", () => {
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
