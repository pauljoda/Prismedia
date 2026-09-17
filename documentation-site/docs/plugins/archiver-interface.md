---
sidebar_position: 6
title: Archiver Executor Interface
description: An independent HTTP contract for URL execution, durable jobs, verified artifacts, and import receipts.
---

# Archiver executor interface

This is the proposed production contract for an Archiver rebuild. Prismedia's adapter
and separate simulator implement single-publication, single-image, and ordered-gallery profiles: URL inspection,
idempotent submission, snapshots, cancellation at the remote API, sealed manifests,
HTTP retrieval, leases, and receipts. The simulator is a single-principal fixture;
production client isolation, incremental reconciliation, lease release, and search remain implementation requirements or optional extensions as
specified below. No changes to an existing Archiver installation are implied.

This document stands alone as a brief for rebuilding The Archiver. Prismedia is the first consumer, but the API should work for any authenticated client. The Archiver need not adopt Prismedia's entities, database, framework, language, or request system.

## 1. Goal and responsibility

A client can submit a source URL or a previously selected source item, recover and monitor the resulting job, retrieve exact output artifacts, and acknowledge successful import. Optional source capabilities add search and incremental discovery.

The Archiver owns source credentials, URL handling, extraction, download execution, packaging, and temporary output retention. The client owns its library placement, metadata approval, final entity mapping, and user-facing acquisition intent.

The API is a small control interface plus a byte-transfer interface. Never stream a whole book/gallery in a JSON plugin response, hold a request open for the entire download, or require a shared database.

## 2. Contract layers

### Required executor profile

- Service identity/version and effective capability discovery.
- Source listing and URL inspection.
- Idempotent job submission and stable operation lookup.
- Durable job snapshots and paginated reconciliation.
- Cooperative cancellation.
- Sealed artifact manifests with HTTP byte retrieval.
- Retention leases and idempotent import acknowledgements.
- Typed errors and bounded request/response sizes.

### Optional capabilities

- Source search, source browse, and child/variant enumeration.
- Shared-filesystem spool delivery.
- Replayable events or webhooks.
- Incremental source change enumeration/subscriptions.
- Pause/resume, specific source actions, and advanced packaging choices.

The initial `single-publication` output profile accepts exactly one selected item
and returns exactly one EPUB/PDF book or CBZ comic content artifact. It includes no
separate artwork or metadata sidecars. Clients and servers must negotiate another
profile before adding those outputs. The simulator publishes its exact OpenAPI
schema at authenticated `/openapi/v1.json`; its independent records live in
`apps/backend/tools/Prismedia.IntegrationSimulator/ArchiverContract.cs`.

A source can be URL-only. Search support must be explicitly declared per installed source, not inferred from its URL patterns. A boolean flag should not imply that every source in an Archiver installation has the capability.

### Optional single-image output profile

Advertise `single-image` in `outputProfiles` to enable standalone image acquisition.
Inspection uses media kind `image`; a selected item declares `png`, `jpg`, or `webp`
as an available format. Submission pins that exact format and profile. Return one
content artifact for the selected item, with a matching extension, exact size, and
SHA-256. The existing retention, resumable retrieval, cancellation, and receipt
requirements apply unchanged.

Prismedia limits this profile to 64 MiB and 100 million pixels and decodes the image
before import. It currently accepts still images only. Animations need a separate future profile. Use `ordered-gallery` for a gallery;
it must not silently become a loose image. The simulator supports `https://fixtures.example/image` with PNG output.
This validates the communication contract; it does not exercise a real image source.

### Optional ordered-gallery output profile

Advertise `ordered-gallery` in `outputProfiles`. Inspection uses media kind `gallery`
and declares `image-set` as its format. Submission selects **one logical gallery**;
`limits.maxItems` remains one and `limits.maxBytes` applies to the sum of all images.
The existing submission JSON fields stay unchanged, preserving old idempotency fingerprints.

Prismedia accepts 1–1,000 JPEG/PNG/WebP still-image content artifacts, totaling at
most 2 GiB. Each image has the single-image profile's 64 MiB and 100-million-pixel
limits. All artifacts must belong to the selected item, share one nonempty `groupId`,
and declare unique contiguous `ordinal` values from 1 through the image count.
The sealed manifest may arrive in a different order. Sidecars, mixed groups, gaps,
and partial results require review; they are never acknowledged as complete galleries.

Every image is retrieved and decoded before placement. Prismedia builds a hidden
folder inside the selected writable, recursive image library, verifies it, and
publishes the whole folder with a directory rename. Retries reuse exact matching
files and reject conflicting content. Deterministic member names preserve the
manifest order on later scans; library titles retain the accepted source names.
Even a one-image gallery retains its container.

Each artifact receipt identifies its actual image owner. Prismedia also retains the
containing gallery locally for navigation; the Archiver need not model that hierarchy.
Acknowledgement follows durable import of **all** members. Lost responses retry the
same receipt without downloading or importing again. The simulator provides
`https://fixtures.example/gallery` (three images) and `/gallery-single` (one image).
These fixtures validate the interface, not real source extraction.

## 3. HTTP surface

Base path: `/api/v1`. JSON is UTF-8; timestamps are RFC 3339 UTC; opaque IDs are strings. Supply credentials through an authorization header, never URL query parameters. The implementation publishes an OpenAPI document and examples matching these semantics.

| Method and path | Contract |
| --- | --- |
| `GET /system` | Persistent server instance ID, API version/range, application version, effective capabilities, limits, retention defaults, health. |
| `GET /sources` | Cursor-paginated installed/enabled sources with IDs, display names, operations, media/format hints, and authentication readiness. No secrets. |
| `POST /inspect` | Check a URL or candidate; return canonical source, compatibility, bounded selection preview, warnings, available output formats, and selection revision/expiry. |
| `POST /search` | Optional. Search a specified source with declared typed fields and bounded pagination; return candidates. |
| `GET /operations/{clientOperationId}` | Recover the job reference for this authenticated client's durable operation ID. Required for ambiguous submissions. |
| `POST /operations/{clientOperationId}/cancel` | Atomically cancel the accepted operation, or persist a tombstone that prevents a delayed POST from creating a job. |
| `POST /jobs` | Validate and atomically persist an operation/job; return `202` with a stable job reference and `Location`. |
| `GET /jobs/{jobId}` | Authoritative snapshot, including terminal status, progress, item outcomes, artifact-manifest reference, and retention state. |
| `GET /jobs?cursor=...&limit=...` | Paginated client-scoped reconciliation; support incremental updated-since/cursor semantics and deleted/expired tombstones. |
| `POST /jobs/{jobId}/cancel` | Request cancellation of queued or running work; no implicit artifact deletion. |
| `GET /jobs/{jobId}/artifacts?revision=...&cursor=...` | Read a consistent sealed manifest revision, with stable pagination and expected artifact count. |
| `GET /artifacts/{artifactId}/content` | Authenticated byte download. Support `HEAD`, byte `Range`, validators, length, and content type. |
| `POST /jobs/{jobId}/lease` | Acquire/renew this client's retention lease; return guaranteed expiration or explicit refusal. |
| `POST /jobs/{jobId}/receipts` | Idempotently record which artifact revisions the client imported; no implicit deletion of unrelated outputs. |
| `POST /jobs/{jobId}/release` | Release this client's retention lease, subject to receipt requirements and retention policy. |
| `GET /events` | Optional cursor-based events; polling must remain sufficient. |

`GET` must not create, cancel, or delete work. Returning a job reference means durable acceptance, not successful download. The system may return a queued job immediately even if no byte size estimate is known.

## 4. Identity and version rules

- `instanceId` is persistent across restarts/upgrades. A genuinely new database/installation gets a new ID. A reset must not silently reuse remote IDs under an unchanged instance identity.
- `sourceId` identifies a configured source in this installation. Include upstream plugin version separately; a plugin upgrade does not change every source ID.
- `clientOperationId` is created by the caller before submission and unique within that caller's credential principal. API-key rotation should retain the principal so in-flight jobs remain accessible.
- `jobId`, `artifactId`, and selection IDs remain opaque. Prefer globally unique IDs; clients must still scope them by instance.
- Keep original source URL, canonical URL, and upstream identities separate from download URLs. URLs may contain expiring tokens and are not durable identifiers.
- API major version changes incompatible semantics; additive optional fields fit a minor release. Unknown required capabilities or major versions are rejected explicitly. Unknown state codes cannot be interpreted as success.
- Pin each job to its source/extractor and relevant configuration revision. Do not change the meaning of an accepted selection because a plugin was updated midway.

## 5. Inspection, search, and selection

Inspection should reveal whether the input represents one item, a finite collection, or a potentially ongoing feed/account. It returns bounded child/variant choices where feasible, not an eager crawl of an entire account.

Candidate facts:

- source and opaque candidate IDs;
- title, canonical origin URL, optional thumbnail;
- optional creator, language, publication label/date, upstream identities;
- single item vs collection and output format hints;
- declared acquisition availability, including unsupported or action-required;
- cursor/has-more metadata for large collections.

Metadata facts are hints, not instructions to create particular Prismedia tables. The client adapter translates upstream identity and grouping into its own model.

An inspection response includes a selection ID, revision, expiry, and selectable item IDs. If inspected content changes, submission must reject the stale revision or use the frozen selection exactly; never silently expand the selection. If enumeration cannot be known beforehand, declare that and require item/byte limits. Reaching a limit is an explicit partial/needs-selection outcome.

Search is source-scoped initially. A client may federate several sources itself and show partial results with per-source errors. No global ranking guarantees are required from The Archiver.

## 6. Submission example

Illustrative request; operation and enum names below belong to the eventual canonical API schema. They are not new inline constants to scatter through Prismedia code.

```http
POST /api/v1/jobs
Authorization: Bearer <integration-token>
Idempotency-Key: 128f1937-a717-4be7-9b47-3a6f531e8163
Content-Type: application/json
```

```json
{
  "clientOperationId": "128f1937-a717-4be7-9b47-3a6f531e8163",
  "input": {
    "url": "https://example.org/comics/a-series/chapter-12"
  },
  "selection": {
    "id": "selection-example",
    "revision": "revision-example",
    "itemIds": ["chapter-release-example"]
  },
  "output": {
    "profile": "single-publication",
    "format": "cbz"
  },
  "limits": {
    "maxItems": 1,
    "maxBytes": 524288000
  }
}
```

Do not accept a client-specified absolute output directory. Files land in an Archiver-owned per-job spool. The client chooses library placement after verified retrieval.

### Idempotency semantics

1. The header and body operation ID must agree. Atomically persist the caller/operation ID, canonical request fingerprint, and job before starting execution.
2. Repeating the same operation and semantic request returns the same job reference and original acceptance result. Concurrent duplicate calls produce one job.
3. Reusing the ID with a different semantic request returns `409`, with a typed idempotency-conflict problem.
4. Resolve the stored idempotency record before rechecking selection expiry: a retry after acceptance must find its original job even when the inspection token has expired.
5. Keep the mapping through job/artifact cleanup. Proposed minimum: 30 days after terminal completion, advertised by `/system`; longer retention is allowed.
6. A genuinely new download/retry intent uses a new operation ID, optionally linked to the earlier job. Clients do not generate a new key merely because a response timed out.
7. If the server cannot determine an old operation's outcome after its documented retention window, report that uncertainty; the client must not assume it was never accepted.

## 7. Job state and progress

Proposed stable states:

| State | Meaning |
| --- | --- |
| `queued` | Persisted, awaiting execution. |
| `running` | Actively resolving, downloading, or packaging. |
| `waiting` | Recoverable wait, with reason and next-action/next-poll hints. |
| `succeeded` | All requested required outputs are sealed and available. |
| `partial` | Finite execution ended with some requested outputs missing/failed or a limit reached. |
| `failed` | Execution ended without satisfying the request. Useful sealed outputs may still exist. |
| `cancelled` | Execution has stopped following cancellation. Previously sealed outputs may still exist. |

Terminal states are immutable. Artifact expiration is a separate retention fact; it does not rewrite successful execution into failure. Recovery or a new run creates a new job with a parent reference.

Snapshot includes: job ID, client operation ID, instance ID, monotonic revision, state, created/updated/start/finish timestamps, cancel-requested flag, source reference, optional stage, counters, item outcomes, warnings/problem, next poll hint, manifest revision, artifact count, and lease/retention timestamps.

Progress may have unknown totals: nullable fraction/bytesTotal/itemsTotal are valid. Report bytes completed and items completed independently where known. Never invent 100% because a source does not provide progress, and do not equate downloaded bytes with completed packaging.

### Cancellation behavior

An operation lookup returning `404` cannot establish that an in-flight POST will
never arrive. The required `cancel-operation` capability closes that race:
`POST /operations/{clientOperationId}/cancel` serializes with submission and returns
`instanceId`, `clientOperationId`, `preventedAcceptance: true`, and an optional `job`.
If no job exists, persist the cancellation tombstone before confirming it. Any later
submission with that operation ID is rejected; cancelling repeatedly returns the
same outcome. Keep tombstones for at least the operation idempotency window.
If a job already exists, return its authoritative snapshot and propagate cancellation.
The client retains ownership until that job is terminal. Never create a new job merely
to cancel an uncertain submission. A new explicit intent requires a new operation ID.


Queued cancellation prevents execution. Running cancellation propagates a signal through source plugins and subprocesses, waits for writes/processes to stop, then records a terminal state. A cancellation request can race with natural success; return the final authoritative state rather than deleting the finished job.

Repeated cancellation is safe. Cancellation does not delete output artifacts or acknowledge import. Unsupported pause/resume is not implemented as cancel-and-redownload under the same job ID.

### Polling and events

Clients honor `nextPollAfter`/`Retry-After`, apply jitter, and back off on outages. Conditional GET with ETag/job revision reduces redundant payloads. Incremental listing must define cursor expiry; after expiry return a reset-required response and let clients perform a full scoped reconciliation.

Events, if implemented, carry event ID, instance ID, job ID, and job revision. Duplicate or stale events are harmless. A disconnected client catches up by snapshots. Webhooks are hints that prompt reconciliation, not proof of current artifact state.

## 8. Artifact manifest

An artifact is immutable bytes with a stable identity, not a guessed filename or a log message. Publish it only after writing/packaging completes and the file is closed. Publish a sealed manifest revision when execution is terminal. Partial jobs have manifests too, plus explicit missing/failed items.

Required artifact fields:

| Field | Purpose |
| --- | --- |
| `id` | Opaque artifact ID, scoped to this instance/job. |
| `relativePath` | Portable suggested name; never an absolute host path or authoritative client destination. |
| `mediaType` | Reported MIME type; client still verifies actual format. |
| `sizeBytes` | Final byte count. |
| `sha256` | SHA-256 of these exact bytes; a source MD5 or archive identity cannot substitute. |
| `role` | Canonical role such as content, cover, or sidecar. |
| `itemId` | Selected output/source item this artifact satisfies. |
| `groupId` / `ordinal` | Optional ordered page/part grouping with explicitly defined one-based ordering. |
| `origin` | Canonical URL and optional upstream identity; no embedded credentials. |
| `contentPath` | API-relative retrieval path using opaque ID. |

Manifest envelope includes job ID, manifest revision, sealed flag, artifact count, and pagination. All pages from a revision are stable. Files cannot change beneath a published hash; new bytes require a new artifact ID/revision. Failed outputs have structured item outcome records, not imaginary zero-byte artifacts.

For comics, prefer one CBZ per independently acquired issue/chapter. Prismedia currently limits an automatic comic-installment import to one file. An optional page-directory output requires an explicitly supported client packaging/import step with correct page order. The current ordered-gallery profile accepts only ordered image content. A metadata sidecar needs a separately negotiated profile and an explicit import policy.

Metadata sidecars are versioned JSON or an advertised standard such as ComicInfo. Preserve source facts, ordering, attribution, language, and identifiers. A sidecar must not instruct the client to execute a command or write outside staging.

### Example terminal manifest

```json
{
  "jobId": "job-example",
  "revision": "manifest-example-1",
  "sealed": true,
  "artifactCount": 1,
  "artifacts": [
    {
      "id": "artifact-example",
      "relativePath": "a-series/chapter-12.cbz",
      "mediaType": "application/vnd.comicbook+zip",
      "sizeBytes": 123456,
      "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      "role": "content",
      "itemId": "chapter-release-example",
      "origin": {
        "url": "https://example.org/comics/a-series/chapter-12"
      },
      "contentPath": "/api/v1/artifacts/artifact-example/content"
    }
  ],
  "nextCursor": null
}
```

The hash and size above are illustrative, not a real artifact checksum.

## 9. Delivery, retention, and acknowledgement

### HTTP retrieval is the baseline

Authenticated artifact retrieval supports length, content type, ETag, and byte ranges. Clients resume against a matching validator and verify the final SHA-256 before import. If bytes are gone, respond with a typed `410` expiration problem; do not return a successful empty file.

Source credentials stay in The Archiver. A client should not need source cookies or OAuth tokens to fetch artifacts. If object-storage URLs are introduced later, authorize a short-lived download ticket and clearly scope allowed origins; do not forward the API bearer token to redirects.

### Shared spool is an optional optimization

Advertise a configured spool ID plus artifact-relative paths, with a ready/sealed marker. The client maps the spool ID to its own mount path. It validates containment, rejects traversal and symlink escape, and verifies size/hash before use. The Archiver cannot supply arbitrary paths into the client's filesystem.

### Retention guarantee

Proposed default: seven days after terminal completion, with renewable leases for active clients. `/system` advertises actual limits. A granted lease guarantees retention until its stated expiry. Under disk pressure, reject new work or lease extensions explicitly; do not silently purge leased artifacts.

Client sequence:

1. Obtain/renew lease before lengthy transfer/import.
2. Transfer, verify, and import artifacts.
3. Commit its local import ledger.
4. Submit an idempotent receipt containing receipt ID, manifest revision, imported artifact IDs/hashes, and an optional opaque client reference.
5. Release its lease for that imported set when appropriate.

Receipt acceptance does not force immediate deletion. The Archiver can retain data according to configured retention, and must not delete artifacts retained by another lease. A receipt for an unknown artifact/revision fails validation. Partial receipts do not authorize cleanup of the remaining files.

If import commits and acknowledgement fails, retry acknowledgement; do not redownload. If import fails, retain outputs and show the failure. No cross-application database transaction is required.

## 10. Error and permission model

Return a typed problem object with stable code, message, retryability, optional retry-after, operation/job reference, and narrowly scoped field details. Distinguish invalid input, unsupported URL/operation, authentication required, source unavailable, rate limited, selection stale, idempotency conflict, storage full, artifact expired, and cancellation.

Messages are for users; clients branch on codes. Third-party HTTP errors map through the adapter. Do not expose stack traces, source tokens, cookies, or local absolute paths in public errors.

Credentials identify a client principal. Minimum conceptual scopes are inspect/search, create jobs, read own jobs/artifacts, cancel own jobs, and manage own leases/receipts. Admin source settings and other clients' jobs are separate. Source credential readiness may be reported, but not the secret values.

Configured network destinations and arbitrary download URLs have different trust. Source adapters validate supported schemes/domains and redirects, while explicitly configured LAN integrations remain possible. Validate archive paths, output sizes/counts, and file types. This is required for correct artifact handling, not an attempt to make process plugins a sandbox.

## 11. Archiver plugin-facing changes

The Archiver may keep its existing plugin language. The external contract only requires that its plugins can provide:

- `inspect` and optional source-specific search/browse;
- execution with cancellation, progress, source settings, and a per-job output context;
- explicit completion records for every produced artifact and failed item;
- canonical source identity and deterministic output grouping;
- bounded output and structured warnings/errors.

Prefer host-owned spool helpers: plugins register finalized files, while The Archiver computes hashes and controls manifest publication. A source plugin's “download succeeded” message should not itself create proof that particular bytes exist.

Source plugin updates must preserve/recover accepted jobs or leave an explicit incompatible-job state. The Archiver should retain the required extractor version for active jobs where feasible.

## 12. Acceptance scenarios for the rebuild

| Scenario | Required observable result |
| --- | --- |
| Submit same request concurrently twice | One job, same returned ID. |
| POST succeeds but response is lost | Operation lookup recovers original job. |
| Restart while downloading | Same job identity resumes or reports an honest recoverable failure. |
| Restart after completion | Job and manifest remain queryable, including after queue UI removal. |
| Cancel running download | Writes/processes stop; final state is queryable; no success-by-row-deletion. |
| URL produces EPUB plus cover/sidecar | Manifest names every artifact and its role. |
| URL produces ordered pages | Ordering survives packaging and retrieval. |
| One of several selected chapters fails | Partial result identifies successful and missing items. |
| Source returns login HTML | Typed source failure; no successful media artifact. |
| Connection drops during artifact GET | Client resumes with validator/range and verifies matching bytes. |
| Selection changes before submission | Explicit stale selection or exact frozen selection execution. |
| Client retries after selection expiry | Original accepted operation still resolves. |
| Client crashes after committed import | Receipt can be retried without acquiring another copy. |
| Retention expires | Metadata/tombstone remains; bytes return explicit expiration. |
| Source plugin changes mid-job | Accepted semantics are preserved or failure is explicit. |
| Unsupported search | Capability is absent; URL acquisition remains usable. |

## 13. Minimum implementation order

1. Durable jobs/operation keys, client authentication, version/capability endpoint.
2. Structured plugin outputs, private spool, sealed manifests, byte retrieval.
3. Cancellation, progress, lease/receipt lifecycle, restart recovery.
4. URL inspection and bounded selections; API contract tests.
5. Optional search, shared spool, event delivery, and subscriptions.

Prismedia can build against a simulator while The Archiver is rebuilt. Completion of items 1–4 establishes the first useful integration: submit URL, monitor, retrieve, verify, import, acknowledge.
