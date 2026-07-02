# Discover / Request — Unified Acquisition Roadmap

This is the direction for Discover/Request. It is intentionally goal-oriented: it says *what each stage
should achieve and why*, not how to write it. Implementation details live in the code and in the review
checks; this document is the north star and the phase order.

## North star

**Requesting media is the Identify flow, run on an entity that does not exist on disk yet.**

Identify takes a real, scanned library entity and fills in its metadata from a plugin. Discover/Request does
the *same thing in reverse*: it starts from a plugin result, builds the library entity from it, and treats the
file as something that *will* arrive. It is a simulation of Identify — near-identical search, review, and
commit behavior — where "Apply" becomes "Request," and the download later attaches the real file to the entity
that was already created.

The user should experience one consistent pattern across the whole app: **search a provider → review what you
found → commit.** Identify commits metadata to a file you have; Request commits a wanted entity you don't have
yet. Same shapes, same UI, same mental model.

## Core principles (do not drift from these)

- **Reuse the real Identify code, don't clone it.** The search, review surface, proposal tree, and
  metadata-apply cascade are shared. If Request and Identify ever diverge visually or behaviorally, that is a
  regression, not a feature.
- **A "wanted" item is a real library entity.** A requested book/movie appears in its normal library grid
  immediately, marked *Wanted*, with metadata and artwork but no playable/readable file. When its download
  imports, the file attaches to that same entity and the Wanted state clears. There is no separate parallel
  "requests database" of shadow records — the library is the source of truth.
- **Generalize once, expand cheaply.** The flow is kind-agnostic. Adding a new media type should mean teaching
  the acquisition side how to *find and grade its releases and lay its files down* — not rebuilding search,
  review, tracking, or the entity model.
- **Containers request and monitor as a unit.** Requesting a top-level container (an author; later a TV
  series) creates and monitors the parent, and fans out to its children. Monitoring the parent means the
  children are tracked under it and new children can be discovered over time.
- **This branch may break and rebuild books.** The existing book request flow is expected to be replaced by
  the unified shape, not preserved alongside it. Avoiding legacy bloat is preferred over compatibility shims.

## Locked decisions

- Wanted entities live **in the library**, badged Wanted (not hidden in a separate area).
- **Cancelling a request deletes its wanted entity** (and its wanted children for a container), per the
  hard-delete-only convention. A monitored-but-not-yet-found item stays Wanted while the monitor searches.
- **Wanted entities are hidden from Jellyfin clients** (Infuse etc.): fileless placeholders never appear
  through the Jellyfin projection, only in Prismedia's own UI.
- **The wanted entity is linked by id, not matched by heuristics.** An acquisition carries the EntityId of
  the wanted entity it fulfils; the import attaches the file to exactly that entity. External-id and path
  matching remain as verification/fallback, never the primary mechanism.
- **The entity's own detail page is the management surface.** Wanted/tracking state (releases, live
  download, monitoring, cancel) is managed on the entity where it lives in the library — the same page a
  real on-disk item uses. `/request` trends toward history/queue only. The same shape later lets a real,
  already-owned entity be *expanded* by monitoring it (an author watched for new books): on-disk and
  in-progress states share one home.
- **Books are rebuilt into the unified shape first** to prove the whole architecture end-to-end (books already
  have a working download/import engine, so no new engine is needed to validate the shape). Movies, music, and
  TV follow as additional kinds.
- The acquisition already carries the media **kind**, so per-kind behavior can branch cleanly.
- External services (Sonarr/Radarr/Lidarr) are gone; Prismedia fulfils everything itself.

## What is already in place

- Search/review/track and the download plumbing are largely kind-agnostic already; the Identify review surface
  and the metadata-apply cascade are reusable; fileless entities and external-id-based entity matching already
  exist. (Groundwork commits: acquisition gained a media `Kind`; entities gained a `Wanted` flag.)

## Phases (books first — each phase is a shippable, reviewed increment)

### Phase A — A request produces a wanted library entity
**Goal:** Requesting an item creates the corresponding library entity up front, fully populated from the
plugin proposal but with no file, and the eventual download lands on that same entity.
**Done when:** a requested book shows in the Books library marked Wanted with real title/description/cover;
when its file imports it attaches to that entity (no duplicate) and the Wanted state clears; a container
(author) request creates the author plus its wanted child books. Reuses the existing metadata-apply cascade
and external-id matching rather than new bespoke import logic.

### Phase B — One review surface, two front doors
**Goal:** The Identify review experience is driven by a shared contract so both Identify and Request render the
exact same components, differing only in where their data comes from and what "commit" does.
**Done when:** Identify continues to work unchanged through the shared components (regression-gated), and the
same components are ready to be driven by a Request-side data source.

### Phase C — Request runs on the shared surface
**Goal:** Discover/Request looks and behaves like Identify: search a provider, pick a candidate, review the
proposal (fields, artwork, children, relationships, drill-in), then Request. Committing creates the wanted
entity/entities (Phase A) and starts the acquisition + monitoring.
**Done when:** the bespoke request page is retired; requesting a book or an author goes through the shared
review surface and produces wanted entities, acquisitions, and monitors.

### Phase D — Containers request and monitor with their children ✅ (books/music)
**Goal:** Requesting a container creates and monitors the parent entity with its children tracked underneath,
rather than a flat pile of independent item requests.
**Done when:** requesting an author yields a monitored author with its wanted books beneath it; the monitor
keeps the children's searches going and can pick up newly discovered children over time.
**Status:** Landed for authors and artists. A container commit auto-monitors the container entity; the
daily sweep re-resolves it from its provider and materializes newly discovered works as wanted
*phantoms* (metadata + artwork, no acquisition — discovery never downloads on its own). A phantom's own
page carries "Search for release", which flows into the ordinary auto-grabbing commit. Real scanned-in
authors/artists are monitorable from their pages once Identify supplies a provider id — on-disk and
requested items share the flow, differing only in how they entered the library. Requested leaves are
hands-off: auto-search → auto-grab best accepted → monitored daily until acquired. TV series will reuse
exactly this shape (phantom seasons/episodes) once the per-episode engine lands.

### Phase E — Prove it and harden it
**Goal:** The unified book vertical is trustworthy end-to-end and the Identify path is provably unregressed.
**Done when:** the create-wanted → attach-on-import → clear-Wanted path, the container fan-out, and the shared
review surface are covered by tests and confirmed end-to-end at the running app; a review pass (internal + a
second reviewer) is clean. **Stop here and evaluate before adding new media kinds.**

## After books prove out (status: multi-kind expansion underway)

The request layer is now registry-driven (`RequestKindRegistry`): search, detail, commit, wanted-entity
creation, per-kind Torznab category routing, and release-engine dispatch are all one descriptor table.
Adding a kind = a registry row + its acquisition-side engine. Current per-kind status:

- **Movies:** ✅ discover → request → wanted Movie entity (full TMDB metadata/cast/artwork) → release
  search graded by resolution/source. ⏳ Remaining: the movie import (lay the video file into a video
  root, bind the wanted movie at scan — the book bind pattern, applied to the video scan).
- **Music:** ✅ artist container → album fan-out (MusicBrainz discography via release-groups; the plugin
  gained artist→album children), release search graded by codec (lossless first). ⏳ Remaining: album
  import + audio scan bind.
- **TV:** discover/detail only (registry `Committable: false`); series → season → episode per-episode
  tracking is the richest container case and lands last, as planned.
- Non-book downloads complete and hold at Downloaded with an honest status until their import engine
  lands — never pushed through the book import planner.

The measure of success held: movies and music reused the whole flow; each needed only a registry row,
an engine, and (for music) a plugin children capability.

## How we verify each phase

- The existing library, Identify, and book-acquisition behaviors stay green (automated tests + the running app).
- Each phase is demonstrated on the running app at the standard local surface, not just in tests.
- New behavior (wanted creation, file attach, container monitoring) gets focused regression tests so later
  phases can't silently break it.
- Ship per phase; keep the book flow working (or intentionally rebuilt) at every commit.
