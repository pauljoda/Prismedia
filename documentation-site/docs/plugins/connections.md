---
sidebar_position: 5
title: Connections and Integration Capabilities
description: Configure independent application instances through installed Prismedia plugins.
---

# Connections and integration capabilities

A **plugin** is an installed package. A **Connection** is one configured application
or catalog using that package. Several connections can use the same plugin with
independent URLs, settings, credentials, and enabled capabilities.

## Configure a connection

1. Install a compatible integration plugin from **Plugins**.
2. Open **Settings → Connections** and choose **Add connection**.
3. Select the plugin, name this instance, and enter the URL reachable by the Prismedia server.
4. Choose the capabilities to enable and enter connection-specific settings and credentials.
5. Save, then select **Test connection** to verify remote support.

Changing configuration requires another test. Disabling a connection revokes its
negotiated capabilities. Saved credentials are never returned to the browser:
leaving a saved credential blank preserves it, and selecting its removal clears it.
A verified application's address is bound to the connection. Create a new connection
for a different address; this prevents existing remote identifiers from being sent
to another server inadvertently.

The connection reports when an application supplies no persistent installation ID.
Such identifiers remain scoped to the configured connection; Prismedia cannot detect
a replaced installation solely from a stable URL. When an application does report
an installation ID, a changed ID blocks negotiated access and requires a new connection.

## Separate responsibilities

| Family | Responsibility |
| --- | --- |
| Metadata | Resolve identities and propose metadata through the existing identify protocol. |
| Catalog discovery | Search, browse, or inspect source items and selectable editions or releases. |
| Acquisition source | Resolve a selection into an acquisition offer. |
| Transfer executor | Submit and reconcile transfers, inspect durable jobs, and retrieve retained output manifests. |
| External manager | Delegate scoped monitoring, acquisition, and file organization. |
| Connected library | Search and inspect externally owned holdings. |

An integration declares only the operations it implements. Metadata search results
are not automatically downloadable items. A manager's completed command is not proof
of a file being available to Prismedia.

Connections provide configuration, capability testing, and a catalog browser at
**Requests → Browse catalogs**. The browser shows only tested connections that
currently advertise catalog browse or search. Administrators can choose a connection and media type,
open catalog sections, search supported sources, and move through result pages.

Catalog pages distinguish full-publication offers from loans, purchases, samples,
and external workflows. These distinctions do not grant permission to execute an
external workflow. Acquisition operations use separate typed orchestration contracts;
declaring an operation alone does not expose a generic executable RPC endpoint.

## OPDS catalogs

The OPDS Catalogs plugin consumes Atom OPDS 1.2 and JSON OPDS 2 catalogs. Configure
the catalog root URL, including a trailing slash when relative links require one.
Authentication can be Basic username/password or a bearer token. Each catalog is a
separate connection. Search is enabled only when the catalog advertises a supported
search template or OpenSearch description.

Navigation and authenticated retrieval are restricted to the configured origin.
Cross-origin downloads, checkout, borrowing, and indirect/DRM acquisition remain
external offers. EPUB/PDF ebook and CBZ/CBR comic catalogs are supported; an unmarked
PDF is treated as a book. This is a consumer interface: the remote catalog remains
the authority for which publications are visible to its configured account.

The discovery API replaces source locators with protected, expiring selection tokens.
Continuation tokens are bound to the connection, media type, query, container, and
page size. Changing a query requires starting a new page sequence. Plugin responses
are bounded and validated before reaching the browser. Resolved download addresses
and authentication headers remain server-only.

### Import a publication

Choose an enabled publication library as the **Import destination**, then select
**Import EPUB**, **Import PDF**, or **Import CBZ** beside a full-content offer.
Loans, purchases, samples, CBR, and formats requiring conversion cannot enter this
importer. Current direct-publication transfers have a 2 GiB byte limit. Archives
also have bounded entry counts, expanded bytes, and format validation.

**Recent imports** shows durable progress and an **Open in library** action after
the exact file has a local Entity owner. Artwork and chapter processing can continue
after source ownership is committed. A failed attempt retains its intent and verified
staging; **Retry import** resumes that same operation. Repeating an acceptance with
the same operation ID does not create another queue run. A different active operation
cannot acquire the same source item through the same connection concurrently.

Prismedia records the selected destination path when accepting the request. Moving,
disabling, or unmounting that library blocks placement. Files receive deterministic,
operation-specific names, and existing files with different bytes are preserved.
Import retries recheck local hashes and do not need the source online once byte
verification has been committed. Keep `/data/integrations/artifacts` until unfinished
imports have recovered; a database receipt alone cannot recreate missing staged bytes.

Accepted intent and the initial background job are committed in one database
transaction. Sensitive source locators in that intent use the same persistent key
ring as connection credentials, with a separate operation-bound encryption purpose.
Connections with transfer history can be disabled but cannot be deleted.

**Cancel download** is available until the worker starts library import. Cancellation
is persisted before stopping the queue run, so an older worker cannot place files
after cancellation wins. Existing staged bytes remain available for inspection.
Once placement can have begun, use **Retry import** to reconcile the existing files.

## Manifest declaration

Manifest v2 accepts an optional `integration` section. Its `protocolVersion` is
independent of metadata identify protocol v2. An integration-only package may declare
an empty metadata `supports` list. Metadata-only packages remain compatible.

```json
{
  "integration": {
    "protocolVersion": 1,
    "capabilities": [
      {
        "kind": "catalog-discovery",
        "operations": ["search", "browse"],
        "entityKinds": ["book"]
      }
    ],
    "settings": []
  }
}
```

Nonsecret `settings` use the existing typed field schema. The manifest's `auth`
fields describe write-only credentials for each connection. Metadata credentials
remain configured through the metadata provider's existing settings.

Effective support is the intersection of package declarations, the remote probe,
the host's supported operations, and the capabilities enabled for this connection.
An undeclared remote operation never grants additional authority. Invalid or duplicate
capability declarations and unsupported integration versions are rejected.

## Probe envelope

Native integration invocations use the same bounded .NET process transport as
metadata, with a distinct envelope discriminator:

```json
{
  "protocol": "prismedia-integration",
  "protocolVersion": 1,
  "invocationId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "operation": "probe",
  "connection": {
    "id": "11111111-2222-3333-4444-555555555555",
    "baseUrl": "http://catalog:8080",
    "expectedInstanceId": null,
    "settings": {},
    "auth": {}
  },
  "input": {}
}
```

Responses echo the protocol, version, and invocation ID. A successful `result`
contains `instanceId` (null if unavailable), `displayName`, optional `version`, and
`capabilities`. A failure returns `ok: false` with an error. Each call returns promptly;
remote polling returns the worker slot to the durable queue between snapshots.

## Credential storage and recovery

Connection credentials use ASP.NET Core Data Protection with a key ring in
`/data/keys/connections` (under the configured data directory). API and worker share
that directory. Each encrypted value is bound to its connection and credential key.
The Unix key directory is owner-only. Filesystem keys rely on those permissions;
this does not protect against access as the Prismedia operating-system user.

Back up the persistent data volume, including this key directory. A database-only
backup does not contain the keys. On another installation, restore both the database
and key directory, or re-enter connection credentials. There is no plaintext fallback
when a key is missing. See Microsoft's
[Data Protection configuration guidance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).

Native plugins remain trusted executable code, not sandboxed extensions. See
[Native process limits and trust](./overview.md#native-process-limits-and-trust).

## Executor and artifact protocol

The integration protocol separates a remote executor job from Prismedia's local
transfer and import state. These are typed adapter contracts; capability declaration
does not itself expose a browser action.

- `submit` receives a durable `clientOperationId`, inspected selection ID/revision,
  explicit item IDs, and item/byte limits. Persist intent before sending it.
- `find-submission` resolves that same operation ID after a timeout or interrupted
  response. A missing active-queue item is never proof of completion.
- `cancel-submission` atomically cancels by operation ID or persists a rejection
  fence for a late POST. A missing lookup alone cannot prove safe cancellation.
- `get-job` reports the persistent installation, stable job ID, operation ID,
  monotonic revision, execution state, progress, item failures, and manifest revision.
- `list-artifacts` reads a sealed revision with an exact total count. Every page
  must retain the job, revision, count, and sealed flag. Repeated cursors, missing
  pages, duplicate IDs/portable paths, and invalid size/hash evidence are rejected.
- `authorize-artifact` provides server-only retrieval instructions for one artifact.
- `renew-retention` guarantees an expiry while transfer or import remains unfinished.
- `acknowledge` confirms a locally persisted receipt ID and exact imported
  artifact hashes. Retrying acknowledgement must not repeat acquisition or import.

Artifact manifests describe opaque IDs, selected item IDs, portable relative names,
MIME types, positive byte sizes, SHA-256 hashes, and content/cover/sidecar roles.
Optional groups use explicit positive one-based ordinals. Suggested names never
choose a host filesystem destination. The host limits a manifest to 10,000 artifacts
and 250 GiB total, with smaller per-request limits where appropriate.

HTTP retrieval stays within the configured origin, including redirects. Headers
are scoped to authorization and accepted media types. Hash-pinned interrupted files
can resume with a validated byte range; a server that returns a full body restarts
the transfer. A verified local receipt is reused only after rechecking the staged
file's size and hash. Unknown-size transfers still have an enforced byte ceiling.
Native plugins remain trusted code; these transport checks do not create an OS sandbox.

Remote execution success permits reading the manifest. Verified bytes permit local
import. Exact committed source ownership permits acknowledgement. Partial, failed, or cancelled execution cannot silently satisfy a full-content request.
Artifact expiration is separate from terminal execution: restored exact outputs can
resume verification, while committed local bytes remain usable without the remote spool.
Direct catalog downloads use the same byte evidence and local ownership rules,
without inventing a remote job or requiring a remote receipt endpoint.


## Connected libraries

**Requests → Connected libraries**, or **Browse library** on a connection, reads existing
holdings through the `connected-library` capability. Search by title or metadata identity,
then inspect the holding's final file associations and external quality profile. An outage
produces an error rather than an empty successful library. Remote file counts and paths do
not establish that Prismedia can read or play those files.

The Radarr adapter supports Radarr 6.x API v3; the Sonarr adapter supports Sonarr 4.x API v3.
Configure the application base URL, including any reverse-proxy prefix, and its API key.
Multiple Connections keep their configuration and identities separate. The current adapters
read existing holdings, profiles, and root folders; they do not issue acquisition or monitoring
commands. Sonarr preserves exact episode-to-file associations, including specials and files
covering several episodes. Manager profile and folder IDs remain external choices.

Neither API supplies a persistent installation UUID. Prismedia reports that limitation and
scopes remote IDs to the Connection. Inspecting an item also verifies its selected metadata
identities so a reused numeric ID cannot silently substitute another holding. A manager's
remote filesystem path requires an explicit mapping before it can become a local library source.

Plugin reads use `search-library`, `get-library-item`, and `manager-options` with typed host
contracts. Pages are limited to 100 holdings; item snapshots to 10,000 files. Adapters reject
unsupported server majors and credential-bearing redirects. Their library-list endpoints
return the whole upstream catalog, so the adapter rejects responses exceeding 8 MiB rather
than silently truncating them. Pagination reflects an observed library that may change between reads.

### Map local files

In **Connected libraries**, choose **Map library folder**, select an existing remote
root, and enter the corresponding folder mounted on the Prismedia server. Use a
dedicated folder outside existing libraries, download areas, and application data.
A read-only container bind mount adds an operating-system boundary to Prismedia's
own file protection.

The mapping creates a watched library with scanning and automatic identification
paused. Enable scanning in **Settings → Libraries** when ready. The external app
continues organizing its files. Prismedia blocks local deletion, replacement, moves,
uploads, and native acquisition destinations in this root. Protection remains when
scanning or the Connection is disabled. Paths are fixed after creation; changing the
Connection's application address or source settings requires a separate Connection.
Root removal is unavailable while the external mapping owns its boundary.

**Check local access** reads fresh remote file associations and checks their mapped
local paths, readability, and sizes. It rejects path traversal and symlinks escaping
the local root. Missing or mismatched files remain explicit failures. Matching size
does not establish a content hash, a completed library import, or playback availability.
Missing external files and empty managed containers retain their catalog records and user
history during scans. Their presence in the catalog does not prove current byte access.

### Track existing holdings

After the initial library scan, inspect a holding and choose **Match existing items**.
Prismedia compares exact mapped source paths, media kinds, and episode coordinates,
including all episodes sharing one file. **Link existing items** saves those reviewed
associations before a worker verifies them again. Conflicting provider identities,
native monitors, unfinished acquisitions, or existing connected ownership prevent linking.

Linking reserves acquisition ownership in the same transaction as the saved intent and
background job. Native searches, retries, replacements, and monitoring cannot take over
that scope. Series and season requests overlap their contained episodes; independent
episodes and book renditions can have separate owners. Known equivalent provider IDs
also prevent duplicate local items from bypassing ownership. Titles alone do not prove
equivalence. Failed acquisitions and paused monitors retain ownership until resolved.

Disabling a connection, a remote outage, or clearing job history does not release its
reservation. Moving between acquisition owners requires an explicit handoff; tracking
does not yet offer that handoff. Database checks also reject metadata edits that would
merge two actively owned scopes.

Tracking observes the connected application every five minutes and can be refreshed
manually. Renames, including a renamed containing folder, and same-scope replacements
retain local entity IDs, file IDs, and user history. Byte-derived metadata and cached
playback assets are refreshed for replacements. Missing or unreadable files retain
their records with an unavailable source role, so catalog presence does not imply
playability. Connection outages retain the last associations and show stale status;
they never start a native fallback acquisition.

Changed target identity, numbering, shared-file coverage, or a conflicting local owner
requires review. Unverified source availability is withdrawn while the association
remains intact. Restoring the expected scope and refreshing tracking can recover it.

Once a root has a tracked holding, video scans delegate to its saved holdings. This
reserves the entire mapped root against path-based discovery, including external folder
renames. Scan the existing collection before linking it. New unscanned holdings and
expanded episode coverage currently require a further explicit import/linking workflow;
they are not silently added by ordinary scans. Tracking does not yet issue manager
requests, change profiles, or enable external monitoring.

## Import from a URL

**Requests → Import from URL** shows tested connections with URL inspection and
transfer execution. Choose a connection and publication kind, inspect the URL, then
select one publication and an enabled destination library. Inspection creates no
remote job. Selections expire, and connection changes require inspecting again.

The initial executor profile imports one complete EPUB/PDF book or CBZ comic,
with a 2 GiB byte limit. Its sealed manifest must contain exactly one content file
for the selected item. Additional covers, sidecars, or pages hold the operation for
review; Prismedia does not acknowledge outputs it has not imported.

A remote executor must provide persistent installation identity, operation recovery,
sealed output manifests, renewable retention, and idempotent receipts. Prismedia saves
the operation before submission, looks up that same operation after an uncertain
response, and resumes polling through durable queue deferrals. A remote job's success
never substitutes for verifying its output bytes.

After local source ownership commits, acknowledgement is a separate step. An unavailable
receipt endpoint leaves **Awaiting acknowledgement**; retries reuse the same receipt
without downloading or materializing the publication again. Partial results remain
held with renewable retention. **Cancel request** is available until local import starts. Prismedia persists the
cancellation before contacting the executor and reserves ownership until remote
execution has stopped. A running transfer observes cancellation at its next durable
boundary. Uncertain submissions reconcile the original operation and use a durable cancellation
fence if no job was accepted;
a never-submitted operation stops locally. If execution finished first, Prismedia
preserves that remote result and cancels the local import without acknowledging its
outputs. Direct catalog downloads retain their **Cancel download** action.

The Archiver adapter targets the proposed executor API v1, including its explicit
`single-publication` output profile. It requires that interface to be implemented;
older Archiver installations that only return success messages are not compatible.
The repository includes a separate [executor simulator](https://github.com/pauljoda/Prismedia/tree/main/apps/backend/tools/Prismedia.IntegrationSimulator)
for synthetic publications and response-loss testing. See the [Archiver interface](./archiver-interface.md)
for a rebuild specification independent of Prismedia's storage model.
