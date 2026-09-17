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
5. Save. Enabled connections are tested automatically; **Test connection** retries the check later.

**Edit** opens an editor for the selected instance. Saving or cancelling returns to
the connection list without inserting a form above it.

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

Use **Request → Browse** to find new titles or browse a connected source. Source
choices follow the selected media type and the operations enabled for that connection.
Administrators can open catalog sections, search supported sources, and move through
result pages. Breadcrumbs return to the source or Request home. **Activity** groups
work needing attention, active requests, followed library items, and recent history.

### Connected does not mean added to your library

| State | What it means |
| --- | --- |
| Application connected | Prismedia can communicate with the source API. Its files may still be unavailable to the Prismedia server. |
| Library folder linked | A source folder has an explicit mapping to an existing folder visible to Prismedia. Scanning determines which titles appear in your library. |
| Files readable | Prismedia has checked the reported files through that mapping. This alone does not create library items. |
| Title followed | A saved association links the external holding to Prismedia library items. Background tracking follows file changes and availability. |

For connected libraries such as Radarr and Sonarr, Prismedia reads media in place.
The external application keeps organizing the original files. A shared folder or
container bind mount provides access; Prismedia does not create a second media copy,
move the source files, or need a symlink for each title. Catalog acquisition and URL
downloads are separate workflows that place newly selected files in a Prismedia
destination library.

Library entity detail pages identify external sources in an **External library**
tab beside the normal detail tabs. It names the connection and library, explains that files are read in
place, and lets administrators return to the connected holding when an exact
association exists. Otherwise they can browse the connected source. Household
viewers can see the source details without receiving connection-management controls.
This provenance comes from saved library associations, so it remains visible when
the application is offline or tracking has been released. Metadata-provider badges
describe metadata identity separately and do not establish file ownership.

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

### Separate file hosts

An acquisition plugin can declare up to eight `integration.anonymousArtifactOrigins`
for catalogs whose files live on a separate HTTPS host. Connection settings show
these additional download hosts. Each declaration is an exact origin, including a
nondefault port when needed; paths, credentials, query strings, and fragments are
not allowed. For example:

```json
"anonymousArtifactOrigins": ["https://files.example.org"]
```

The host checks the current installed declaration when resolving a selection and
again before retrieving its bytes. Cross-origin retrieval requires an empty delivery
header dictionary and sends no cookies. Redirects remain confined to the selected
origin, even when another origin is also declared. Removing an origin blocks pending
downloads that still need it; already verified local bytes can finish importing.
Authenticated same-origin downloads keep their existing behavior. This declaration
applies only to acquisition sources; executor artifacts remain connection-scoped.
It does not turn OPDS cross-origin offers into downloads automatically.

### Suwayomi downloaded chapters

Suwayomi Server's OPDS catalog can expose existing downloaded chapters through the
OPDS Catalogs plugin. Configure a connection with its server URL followed by
`/api/opds/v1.2`, and supply the server's Basic authentication credentials when enabled.
Use the address reachable from Prismedia, including its container network if applicable.

In **Browse catalogs**, select **Installments**, search the library or open a
series, then open a chapter's metadata entry and choose **Import CBZ**. A chapter
must already be downloaded in Suwayomi before its full-content offer appears.
Enable CBZ downloads in Suwayomi and keep its OPDS **Mark as read on download**
setting disabled if importing should leave Suwayomi's reading progress unchanged.

This connection copies the selected publication into the chosen Prismedia library.
It does not enqueue missing chapters, install source extensions, synchronize reading
progress, or delegate series monitoring. Suwayomi owns its original files; Prismedia
owns the imported copy. Undownloaded chapter entries can be browsable without offering
a file. Existing-library retrieval has been exercised against Suwayomi Server 2.3.2243;
remote-source downloading requires a separate manager or executor integration.

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
Book and comic display titles use embedded publication titles first, then the selected
source title when the retained import record matches the exact path and bytes. This
keeps storage operation IDs out of display names during initial import and later scans;
ordinary files and unverified replacements retain the normal metadata/filename fallback.
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

## Image catalogs

Catalog acquisition also accepts JPEG, PNG, and WebP still images. Choose an enabled,
writable library with image scanning; book-only and externally managed roots are not
image import destinations. Images have a 64 MiB compressed-byte limit and bounded
dimension and still-image validation. Source-provided SHA-1 checksums can pin a file
version; Prismedia always computes and retains its own SHA-256 import evidence.

The **Wikimedia Commons** plugin searches the public catalog without an account or
API key. Set its connection URL to `https://commons.wikimedia.org`, enable catalog
discovery and acquisition, test the connection, and search under **Browse catalogs**.
Its additional anonymous file host is `https://upload.wikimedia.org`.

Commons selections pin the page and uploaded file version. If that version changes
before import, search again to select the current file. Unsupported formats and
oversized images are omitted from results; a page can be empty while still offering
another results page. Search does not add a content-rating filter.

When a catalog supplies attribution, **Source attribution** shows its creator,
credit, license and usage statements, with source and license links. Accepted
metadata is retained in the encrypted import record and remains visible in
**Recent imports** and the imported library item's source details, including image
viewer details, after restarts and retries. Library details use completed import
receipts for that exact entity and follow its normal access restrictions; they do
not contact the source service. Later source changes do not rewrite that accepted
snapshot or curated library metadata. Older imports that predate this capture have
no attribution snapshot. If saved information cannot be decrypted, details report
that attribution is unavailable while keeping the library item accessible.

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
Only keys declared by the current plugin manifest are decrypted for an invocation.
The executable boundary checks that declaration again before passing credentials.
Fields retired by a plugin update remain encrypted for recovery, and unreadable
retired values do not block the credentials still in use.
Metadata plugin and OpenSubtitles credentials share this key ring under a separate
provider-specific purpose. Existing unencrypted provider rows are upgraded at startup;
concurrent credential changes take precedence over that upgrade.
The Unix key directory is owner-only. Filesystem keys rely on those permissions;
this does not protect against access as the Prismedia operating-system user.

Back up the persistent data volume, including this key directory. A database-only
backup does not contain the keys. On another installation, restore both the database
and key directory, or re-enter connection and provider credentials. There is no plaintext fallback
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

**Request → Browse**, or **Browse titles** on a connection, reads existing
holdings through the `connected-library` capability. Search by title or metadata identity,
then inspect the holding's final file associations and external quality profile. An outage
produces an error rather than an empty successful library. Remote file counts and paths do
not establish that Prismedia can read or play those files.

The Radarr adapter supports Radarr 6.x API v3; the Sonarr adapter supports Sonarr 4.x API v3.
Configure the application base URL, including any reverse-proxy prefix, and its API key.
Multiple Connections keep their configuration and identities separate. Both adapters
read existing holdings, profiles, and root folders. Both support the reviewed
controls described below, with different authority for movies and episode scopes.
Sonarr preserves exact episode-to-file associations, including specials and files
covering several episodes. Manager profile and folder IDs remain external choices.

Radarr 1.4.0 and Sonarr 1.3.0 plugins also supply optional poster, backdrop, overview,
genre, runtime, and certification metadata. Connected titles use the same detail
layout as library titles, with separate overview, file, and library-link sections.
Previewing a title does not create a library entity or overwrite curated metadata.
Artwork uses the source's public HTTP(S) remote image URLs; authenticated local cover
paths and API keys are not sent to the browser. Missing artwork uses the normal
Prismedia placeholder. A backdrop is shown only when the source explicitly supplies one.

The optional `ManagedLibraryItem.presentation` contract carries `overview`, `posterUrl`,
`backdropUrl`, `genres`, `runtimeMinutes`, and `contentRating`. Existing adapters may
omit it. The host bounds and validates these fields independently of file evidence;
presentation metadata never establishes ownership or file availability.

Neither API supplies a persistent installation UUID. Prismedia reports that limitation and
scopes remote IDs to the Connection. Inspecting an item also verifies its selected metadata
identities so a reused numeric ID cannot silently substitute another holding. A manager's
remote filesystem path requires an explicit mapping before it can become a local library source.

Plugin reads use `search-library`, `get-library-item`, and `manager-options` with typed host
contracts. Pages are limited to 100 holdings; item snapshots to 10,000 files. Adapters reject
unsupported server majors and credential-bearing redirects. Their library-list endpoints
return the whole upstream catalog, so the adapter rejects responses exceeding 8 MiB rather
than silently truncating them. Pagination reflects an observed library that may change between reads.

### Kapowarr comic libraries

The Kapowarr plugin connects to existing **Kapowarr 1.3.x** libraries. Configure
its base address and API key, then use **Connected libraries** to search existing
comic runs and inspect their final files. Exact issue labels such as `½` and
`12.5` remain visible; one archive may cover several issues. Cover files and other
sidecars are excluded. A downloaded-issue count is not presented as a file count.

Its external-manager capability currently lists root folders. Map a root to a
dedicated read-only local folder, check access, and enable its library scan when
ready. Kapowarr continues organizing those files. Comic acquisition ownership,
monitoring controls, release searches, and upgrade reconciliation are not yet
offered. Multiple renditions for one issue require review in Kapowarr.

The plugin does not need a Comic Vine account to read existing holdings; adding
new runs inside Kapowarr depends on that application's own catalog configuration.
Kapowarr supplies no persistent installation UUID, so keep a connection pointed
at the same installation. Its API requires a query-string API key; the adapter
keeps that address server-side and refuses redirects. Reverse-proxy access logs
should omit query strings.

### Map local files

Open a connection's library-folder settings and choose **Map library folder**, select an existing remote
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

Opening a connected title automatically checks its mapped local paths, readability,
and sizes while the source details remain visible. **Refresh** repeats this check.
If no mapping covers the reported files, the page offers library-folder setup;
closing that setup rechecks availability. A failed check leaves the title visible
with availability unknown. The check rejects path traversal and symlinks escaping
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

Tracked holdings expose stable `targets` independently of their current file `bindings`.
Each target pins its remote identity, coordinates, and local entity. Replacing or losing
a file does not redefine that scope; manager controls verify that retained targets,
file associations, and fulfillment ownership still agree before dispatch.

Linking reserves acquisition ownership in the same transaction as the saved intent and
background job. Native searches, retries, replacements, and monitoring cannot take over
that scope. Series and season requests overlap their contained episodes; independent
episodes and book renditions can have separate owners. Known equivalent provider IDs
also prevent duplicate local items from bypassing ownership. Titles alone do not prove
equivalence. Failed acquisitions and paused monitors retain ownership until resolved.

Disabling a connection, a remote outage, or clearing job history does not release its
reservation. Moving between acquisition owners requires the explicit handoff below.
Database checks also reject metadata edits that would
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
renames. Scan the existing collection before linking it. Wanted-movie requests use the
explicit workflow below to attach newly available files. Other unscanned holdings and
expanded episode coverage require a further import/linking workflow; ordinary scans
do not silently expand the owned scope.

### Release an acquisition owner

Radarr plugin 1.3.1 and Sonarr plugin 1.2.1 provide handoff inspection. Test the Connection
again after upgrading. Turn off monitoring for the exact linked movie or episodes,
let downloads and commands settle, then select **Review ownership handoff** on its
tracked holding. Review the work, monitoring, and activity, acknowledge keeping that
scope unmonitored, and choose **Release acquisition owner**.

Acceptance freezes new Prismedia controls, requests, and source reconciliation for
this holding while retaining its owner. The worker reads the same pinned identities
again, verifies the reviewed path, and checks monitoring and activity before releasing
ownership. **Handoff pending** retains ownership through an outage or changed evidence;
**Refresh handoff** retries observation without sending remote mutations. Routine file
observations do not invalidate a review when its target identities remain unchanged.

The initial adapters conservatively require the application's entire download queue
to be empty, including unrecognized items, and all reported commands to be terminal.
Reported download-client health problems also block release, since a client outage
can hide activity from the queue. These health observations can lag a new outage.
Activity for another work can therefore delay a handoff. Missing or unknown activity
is never evidence that work stopped. Unfinished Prismedia actions and actions closed
with an unverified outcome block release; closing an uncertain action is not a way to
discard its ownership risk.

**Ownership released** stops tracking and preserves the source files, local items,
user history, and archived associations. A wanted request also stops, keeping its
existing local identity. The mapped folder remains read-only. Choose another owner
explicitly afterward, or link the existing files again with a new reviewed association.
Replaying the accepted handoff returns its existing result.

These APIs provide observations rather than a remote transactional lock. Keep the
old scope unmonitored and avoid starting work directly in the connected app during
handoff. Prismedia does not delete remote holdings, cancel unrelated jobs, move files,
or configure a replacement owner as part of release.

### Request a wanted movie through Radarr

Radarr plugin 1.2.0 or later supports exact movie lookup and initial creation. Test
the Connection again after upgrading. In **Connected libraries**, choose **Request
a wanted movie**, select an existing wanted movie with a TMDB identity, and choose
an enabled mapped video library. Review its external profile, monitoring choice,
and optional **Search now**, then select **Request through manager**.

The item must have no retained source, native acquisition, native monitor, or other
fulfillment owner. Existing files use the linking workflow above. Existing Radarr
holdings must already belong to the selected mapped root and use the selected
profile; initial delegation never moves them or overwrites another profile.

Acceptance saves the request, exclusive acquisition owner, and first background job
together. New Radarr movies are created unmonitored without an automatic search;
separate durable manager actions then apply the explicit monitoring/search choices.
Existing exact holdings are reused. A lost or malformed creation response preserves
uncertainty: subsequent observations look up the same TMDB identity and never send
another creation automatically. **Refresh request** reconciles that retained intent.

**Cancel request** releases ownership only before creation can have run or after a
definite rejection. Once a holding may exist, cancellation cannot establish that
the remote app has stopped managing it; ownership remains until an explicit handoff.
Disabling a connection and removing queue history do not release the request.

**Waiting for files** creates no fictional source records. The worker verifies the
exact remote holding and final file association, mapped boundary, local readability,
and expected size before attaching a source to the original wanted entity. This
preserves its title, organization flag, and user history. Size/path checks do not
claim a content hash. The established holding tracker handles later replacements
and missing files. **Imported into Prismedia** records that import occurred; current
availability is shown by the tracked holding separately.

Administrators can also begin in **Requests → Discover**. On a movie review with an
exact TMDB identity, choose a tested manager under **Acquisition owner**, then
**Save metadata and review manager request**. This saves the selected metadata as a
wanted movie without a native acquisition or monitor. The next form reviews the
external library, profile, monitoring, and search choices. Leaving before submitting
that form keeps the wanted movie available for later selection; it does not start
external fulfillment. An already-owned movie instead links to its existing library
record and uses the existing-file matching workflow.

The metadata preparation validates the complete reviewed proposal and the exact
enabled metadata-plugin identity route. Accepted metadata and external fulfillment
are separate decisions. Once metadata is saved, this screen shows the manager request;
subsequent metadata changes belong to the saved library item's review tools.

### Control a linked Radarr holding

Install Radarr plugin 1.1.1 or later and test its Connection again to negotiate the
new operations. Open **Manager controls** on a linked holding, then **Review manager
settings**. Choose an explicit profile or monitoring change, optionally select
**Search now**, and choose **Apply manager action**. Omitted settings are preserved.
Changing monitoring alone does not issue an immediate search.

Prismedia derives the target scope from the saved associations and fulfillment owner.
It checks the pinned metadata identity, reviewed folder, and relevant settings again
before changing the manager. This workflow never creates a new remote movie, moves
files, or removes an existing holding. Only one unfinished action can control a
holding at a time; different configured instances remain independent.

The accepted action and first background run commit together. Each write has a
durable dispatch fence. A timeout or crash after that fence leaves an uncertain
outcome: settings recover by observing the requested values, while a search without
its returned command identity is never automatically sent again. Radarr does not
provide idempotency keys or atomic compare-and-set for these writes, so a concurrent
edit in Radarr can still race the adapter's fresh checks.

An acknowledged search retains its command ID **and original queue timestamp**.
Missing history, changed timestamps, or mismatched command scope remain unverified.
**Search completed** describes execution only: a search can finish without finding
a release. Existing tracking separately verifies final files and local byte access.

**Cancel unsent stage** stops only a stage that has not crossed its dispatch fence.
Previously confirmed settings stay applied. **Refresh action** observes retained
progress. For an unresolved write, inspect the connected app before acknowledging
**Close with outcome unverified**. Closing stops observation, does not undo settings
or cancel remote work, and keeps the holding's fulfillment ownership. Creating a
new action afterward is a new explicit request.

Action history survives queue-history removal and connection outages. These controls
use typed `reconcile-managed`, `configure-managed`, and `request-managed` contracts;
plugins cannot expose arbitrary manager endpoints through this API.

### Control linked Sonarr episodes

Sonarr plugin 1.1.0 or later uses the same durable action workflow for the exact
episodes already linked to a holding. It pins the series identity and each episode's
remote ID, season, episode number, and known absolute number. New episodes and changed
coverage require explicit association review before entering that scope.

**Search now** sends one `EpisodeSearch` for those episode IDs, including specials.
It can search while the series is unmonitored and leaves all monitoring flags intact.
It never substitutes a full-series or season search command. The manager still owns
release selection; downloaded packs can contain additional content, which does not
automatically expand Prismedia's linked scope.

Episode monitoring changes require the parent series to be monitored already.
Otherwise the control explains that restriction and remains unavailable. The series
profile is also read-only here because it affects episodes outside the linked scope.
Prismedia never enables the parent gate implicitly. Monitoring changes use only exact
episode IDs and preserve unrelated episode flags, profile, folder, and files.

## Import from a URL

**Requests → Import from URL** shows tested connections with URL inspection and
transfer execution. Choose a connection and media kind, inspect the URL, then
select one item and an enabled destination library. Inspection creates no
remote job. Selections expire, and connection changes require inspecting again.

The publication executor profile imports one complete EPUB/PDF book or CBZ comic,
with a 2 GiB byte limit. The optional image profile imports one JPEG, PNG, or WebP
still image, limited to 64 MiB and 100 million pixels. Images must decode completely,
match their filename format, and use a writable library with image scanning enabled.
Animated images are not supported by these profiles. Ordered galleries use their own profile below.

For publication and standalone-image profiles, each sealed manifest contains exactly one content file
for the selected item. Additional covers, sidecars, or pages hold the operation for
review; Prismedia does not acknowledge outputs it has not imported.

A remote executor must provide persistent installation identity, operation recovery,
sealed output manifests, renewable retention, and idempotent receipts. Prismedia saves
the operation before submission, looks up that same operation after an uncertain
response, and resumes polling through durable queue deferrals. A remote job's success
never substitutes for verifying its output bytes.

After local source ownership commits, acknowledgement is a separate step. An unavailable
receipt endpoint leaves **Awaiting acknowledgement**; retries reuse the same receipt
without downloading or materializing the item again. Partial results remain
held with renewable retention. **Cancel request** is available until local import starts. Prismedia persists the
cancellation before contacting the executor and reserves ownership until remote
execution has stopped. A running transfer observes cancellation at its next durable
boundary. Uncertain submissions reconcile the original operation and use a durable cancellation
fence if no job was accepted;
a never-submitted operation stops locally. If execution finished first, Prismedia
preserves that remote result and cancels the local import without acknowledging its
outputs. Direct catalog downloads retain their **Cancel download** action.

Gallery imports require a writable image library with recursive scanning enabled.
They accept one complete gallery of up to 1,000 ordered JPEG/PNG/WebP still images,
64 MiB per image and 2 GiB total. Prismedia verifies every image before publishing
one folder, preserves the source order through rescans, and opens the resulting
gallery from the transfer. A one-image gallery keeps its gallery identity. Missing
members, inconsistent ordering, and extra sidecars remain held for review without
acknowledgement. Retries preserve existing files and image identities.

The Archiver adapter targets the proposed executor API v1, including its explicit
`single-publication`, optional `single-image`, and optional `ordered-gallery` output profiles.
Image and gallery choices appear only when the server advertises their profile. It requires that interface to be implemented;
older Archiver installations that only return success messages are not compatible.
The repository includes a separate [executor simulator](https://github.com/pauljoda/Prismedia/tree/main/apps/backend/tools/Prismedia.IntegrationSimulator)
for synthetic publications, images, galleries, and response-loss testing. See the [Archiver interface](./archiver-interface.md)
for a rebuild specification independent of Prismedia's storage model.
