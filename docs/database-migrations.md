# Database Migration Policy

Prismedia uses EF Core migrations as an append-only upgrade history. A normal release adds a
reviewed migration and never edits, renames, or removes a migration that may have reached a user.
The API remains the sole migration owner; the worker waits for the API to finish.

Accumulated migrations are primarily source-history cost, not runtime schema bloat. PostgreSQL
stores the final schema plus a small `__EFMigrationsHistory` table; it does not retain every old
table shape. A long migration list alone is therefore not a reason to rewrite deployed history.

## Normal releases

- Generate migrations from the current EF model and inspect both `Up` and `Down`.
- Keep every published migration immutable and in order.
- Exercise empty-database and upgrade-from-last-release paths before publishing.
- Never mix `EnsureCreated` with migration-managed databases.
- Reject migration identifiers unknown to the running build. Downgrading a database with newer
  schema history is unsupported and must fail loudly.

## Schema epochs

A baseline collapse is allowed only as a deliberately coordinated schema epoch, normally at a
major release boundary. It is a compatibility operation, not routine cleanup.

Use two releases:

1. **Bridge release.** This build still contains the complete legacy history. It migrates every
   supported installation to one named cutoff migration. Users on older builds must install this
   release before crossing the epoch.
2. **Baseline release.** Generate one baseline migration from the exact cutoff model after moving
   the legacy migration sources out of the active migration assembly. The baseline `Up` creates the
   complete current schema for an empty database.

The baseline release must recognize only these starting states:

- **Empty database:** apply the new baseline normally.
- **Exact legacy cutoff:** verify that the ordered applied-migration set exactly matches the bridge
  release, verify there are no unknown or pending legacy migrations, then transactionally replace
  the legacy `__EFMigrationsHistory` rows with the one baseline row without executing baseline
  `Up`. The existing schema and data are already the baseline.
- **Current/new epoch:** continue applying migrations after the baseline normally.
- **Anything else:** refuse startup with an actionable instruction to install the bridge release
  first. Never guess from table presence, migration count, or application version.

Take a database backup before the history rewrite. Keep the bridge release and its migration source
available from its release tag so an installation can always be brought to the cutoff.

## Required epoch validation

An epoch is publishable only when automated PostgreSQL checks prove:

- empty database to baseline/current succeeds;
- exact legacy cutoff to baseline/current preserves representative data;
- older, newer, partial, reordered, and unknown histories are refused;
- the schema produced by the baseline is equivalent to the schema produced by the full legacy
  chain at the cutoff;
- a failed history rewrite rolls back atomically; and
- API/worker startup coordination still leaves the API as the only schema writer.

Until those checks and the two-release bridge exist, keep appending migrations. Prismedia's current
fresh-install migration time is small enough that a collapse is optional cleanup, not a performance
requirement.
