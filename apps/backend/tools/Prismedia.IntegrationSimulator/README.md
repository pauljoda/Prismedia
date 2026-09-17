# Archiver executor simulator

A separate reference executor for Prismedia integration tests. It does not modify or
embed The Archiver. Its HTTP records are independent of Prismedia's entities and database.

Run with .NET 10, an **isolated** data directory, and a token of at least 24 characters:

```sh
SIMULATOR_DATA=/tmp/archiver-simulator \
SIMULATOR_TOKEN='<generated test token>' \
ASPNETCORE_URLS=http://127.0.0.1:19090 \
dotnet run --project apps/backend/tools/Prismedia.IntegrationSimulator
```

Supply `Authorization: Bearer <token>` on all endpoints, including `/openapi/v1.json`.
Install the `archiver` integration plugin and configure a Connection with the simulator's
base address and token. Enable discovery and transfer execution; test the connection.
Use **Requests → Import from URL** with:

- `https://fixtures.example/book` — original synthetic EPUB;
- `https://fixtures.example/comic` — original synthetic one-page CBZ;
- `https://fixtures.example/image` — synthetic PNG through the separate `single-image` profile.

No arbitrary source URL is fetched. The generated files, persistent installation ID,
selections, operation mappings, jobs, manifests, and receipts stay in the configured
directory. Run only one simulator process against that directory.

## Failure injection

`POST /simulation/controls` accepts these simulator-only fields:

```json
{
  "loseNextSubmissionResponse": true,
  "loseNextReceiptResponse": true,
  "executionDelaySeconds": 15
}
```

Each lost response aborts the socket **after** its durable commit. Restart the process
with the same directory to test recovery. Repeated submissions must find one job;
receipt retries must retain their ID and exact hashes. Delay makes queued/running
cancellation and mid-execution restarts observable.

The fixture supports authenticated GET/HEAD/Range artifact retrieval, immutable SHA-256
manifests, finite byte limits, idempotent cancellation, durable operation cancellation tombstones, and renewable retention. It keeps
operation history indefinitely and marks expired bytes unavailable without rewriting
execution state. It never deletes another application's files.

## Deliberate limits

This is a single-principal contract test fixture, not a production download service.
It has no source credentials, search, arbitrary downloads, multi-client authorization,
retention cleanup, subscriptions, release endpoint, incremental change feed, or gallery
packaging. Its JSON document is a small test persistence mechanism, not a recommended
production database. The independent HTTP records and generated OpenAPI provide the
starting interface; the published integration documentation defines broader semantics.

```sh
dotnet test apps/backend/tests/Prismedia.IntegrationSimulator.Tests
```
