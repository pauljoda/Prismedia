# Archiver acceptance checker

This standalone .NET 10 tool checks the reference simulator or a separately built
Archiver through the proposed public HTTP interface. The shared
`Prismedia.ArchiverContract` project contains only wire records and constants;
neither tool needs Prismedia's database or domain model.

Set `ARCHIVER_TOKEN` in your environment. Credentials are never accepted on the
command line or written into reports. Without an execution option, the tool only
reads the system endpoint:

```sh
dotnet run --project apps/backend/tools/Prismedia.ArchiverConformance -- \
  --endpoint http://localhost:19090
```

## Bounded execution

Use an isolated service and a source fixture you are allowed to retrieve. This
creates one real job, downloads its output, and acknowledges those bytes. The
checker limits the selection to one source item, 32 MiB total and 100 artifacts,
with a three-minute deadline. It never imports into a Prismedia library.

```sh
dotnet run --project apps/backend/tools/Prismedia.ArchiverConformance -- \
  --endpoint http://localhost:19090 --execute \
  --input https://fixtures.example/book --kind book --format epub \
  --output-directory .tmp/archiver-acceptance
```

The simulator also supports `comic/cbz`, `image/png`, and `gallery/image-set` at
the corresponding fixture URLs. A real service needs its own valid fixture URL.

Each run writes a private operation directory containing:

- `plan.json`: endpoint, installation identity, exact submission and stable receipt
  ID, saved before the first submission. This file contains source details; keep
  the directory private and outside version control.
- `artifact-NNN.bin`: bytes verified against the sealed manifest, flushed to disk
  before any import receipt is sent. Manifest paths are never used as local paths.
- `report.json`: checks passed, only written after all checks complete.

Checks cover identical submission replay, recovery by operation ID, rejection of
changed submissions, cancellation before acceptance, job identity/revisions,
sealed and paginated output manifests, size/hash verification, HEAD/Range
retrieval, retention leases and repeatable receipts. Redirects are disabled and
artifact URLs must stay within the configured API origin and base path.

## Restart and lost-response checks

Stop and restart the executor with its retained state, then resume a saved plan:

```sh
dotnet run --project apps/backend/tools/Prismedia.ArchiverConformance -- \
  --endpoint http://localhost:19090 \
  --resume .tmp/archiver-acceptance/<operation-id>/plan.json
```

Resume reuses the original operation and receipt IDs and requires the original
installation identity. It also works after a timeout or lost submission/receipt
response. It retrieves and verifies the fixture again; it does not create a second
job or a second receipt. The simulator's `/simulation/controls` endpoint can inject
these failures; it is intentionally not part of the checker or production API.

Exit code `0` means the reported checks passed, `1` means an assertion or transport
failed, and `2` means command-line configuration is invalid. A probe is not an
execution pass. This finite check does not establish multi-client isolation,
production cleanup, source credential security, or all failure scenarios: use the
published Archiver interface's acceptance checklist for those requirements.
