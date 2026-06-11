# Plugin Saturation Audit — 2026-06-09

Real-data validation of every installed metadata plugin against the local dev
library, plus a source-level comparison of what each plugin emits versus what its
upstream API offers, and what the backend apply pipeline persists versus drops.

Method: for each plugin, run live `POST /api/identify/entities/{id}` lookups and
searches against real library entities, apply the proposals, verify every persisted
row in Postgres, and diff the plugin source against the upstream API surface.

Tested live: **TMDB 1.1.5** (movie, series cascade, person, studio), **YouTube
1.2.1** (video by bracketed id / url / search, music artist with 203-album cascade,
album, track), **MusicBrainz 1.1.1** (artist, album with track children, track),
**MangaDex 1.1.3** (book cascade over 20 volumes / 185 chapters, NSFW oneshot,
volume- and chapter-level identify), **AniList 1.0.1** (JJK series cascade with
relation-BFS season mapping, season identify — tested 2026-06-10 after the upstream
outage lifted).

## Fix status (2026-06-10)

Fixed and verified live:
- **P0-1/P0-2/P0-3** (below): replace-path deletion, transient-apply pipeline
  unification, and partial-precision dates — fixed in `fix(api): stop identify
  applies from deleting re-sent dates, stats, and positions`. Verified: double-apply
  preserves dates/stats with `plugin` provenance, ISO timestamps and year-only dates
  apply with `precision`, cross-provider external ids survive applies (confirmed
  independently by the AniList test). The single-source structural-children dedup
  also landed.
- **MangaDex 1.1.4**: volume-scoped chapter matching (explicit Ch.N title numbers
  preferred, global sort+1 heuristic removed), external-reader stub filtering,
  fallback-language feeds keep structure but emit neutral `Chapter N` titles instead
  of another language's, scoped-fallback no longer leaks book dates/urls (confidence
  0.3), and MusicBrainz-style 250 ms cross-process rate limiting (the unthrottled
  plugin got the dev IP temporarily blocked by MangaDex's edge during this audit).
  Verified on TPN: all 185 chapters bind to their correct local files across 20
  volumes; the previously corrupted chapters were repaired from archive filenames.

Still open: everything in §3 per-plugin drops, §4 model gaps, §5 new sources, and
the AniList plugin issues below.

---

## 1. Cross-cutting backend bugs (fix before any plugin work)

### P0-1 — Apply deletes existing dates/stats/positions (data loss, every apply)
`EntityMetadataApplyService.ScalarFields.cs:151-157, 171-177, 198-204`.
`ReplaceDatesAsync`/`ReplaceStatsAsync`/`ReplacePositionsAsync` do
`RemoveRange(existing)` then call the Upsert helper, whose `FindAsync` returns the
*tracked, Deleted* row from EF's identity map; mutating it does not resurrect it, so
`SaveChanges` deletes the row instead of updating it. Net effect: any date/stat/
position code that already exists on the entity is hard-deleted whenever the patch
re-sends the same code — i.e. the root entity of **every** identify apply loses the
very rows the apply is trying to write. Proven 3× on a movie (toggle on/off across
repeated applies), plus a series root, a person birth date, and a MusicBrainz album.
Fix: resurrect state (`Entry(existing).State = Modified`) or skip removal for
re-sent codes.

### P0-2 — Two divergent apply pipelines; the transient endpoint uses the wrong one
`IdentifyEntityEndpoints.cs:41-50` routes `/api/identify/entities/{id}/apply`
through `ApplyPatchAsync` (manual-edit Replace semantics) instead of the
proposal-aware `ApplyAsync` (Upsert semantics). Consequences, all verified:
- P0-1 fires on the root entity (children go through Upsert and are safe).
- `ReplaceExternalIdsAsync` wipes **all providers'** external ids before re-adding
  (HBO lost its `tmdbNetwork` id when re-identified as a TMDB company).
- Classification provenance is stamped `manual` instead of `plugin`.
- NSFW propagation to root-linked tags/credits/studio is skipped (`Romance` tag
  stayed `is_nsfw=f` after applying an NSFW MangaDex proposal); queue-accept and
  auto-identify do propagate. Same proposal, different outcomes per surface.

### P0-3 — Date validation is both too strict and inconsistently enforced
`EntityMetadataPatchValidator.cs:45` requires `DateOnly.TryParse`, so:
- YouTube's ISO datetimes (`published`/`uploaded`) → selecting `dates` at the root
  400s (`invalid_entity_metadata_patch`) — dates can never be applied from the UI.
- Year-only values (YouTube album `released: "2025"`, MangaDex `published: "2016"`)
  → 400 at the root, but **cascade children bypass validation entirely** and store
  the same values with `sortable_value = NULL` (exists but unsortable).
`entity_dates` already has a `precision` column — wire it up: accept ISO datetimes
(truncate) and partial dates (year / year-month) with explicit precision, and run
the same validation on child nodes.

### P1 — Structural-children merge and ordering
- `IdentifyPluginService.StructuralProposals.cs:185-191`: when provider children are
  empty the merge early-returns local children **without dedup/normalization** —
  duplicate child nodes for the same `TargetEntityId` get applied twice.
- `EntityMetadataApplyService.ScalarFields.cs:206-218`:
  `ApplyStructuralSortOrderAsync` has no uniqueness guard — produced two chapters
  with `sort_order=0` in one volume.
- Sort-basis inconsistency: volumes flipped 0-based → 1-based after apply (position
  code preferred over scan order) while chapters stayed 0-based; plugins doing
  `sortOrder+1` arithmetic then mis-match.

### P1 — Misc backend
- NSFW entities return “Entity … was not found” from identify under the default
  `hideNsfw` (`IdentifyPluginService.cs:56-59`) — misleading error.
- `requireChoice=true` is defeated when the entity has an identified parent:
  `CascadeFromParentAsync` discards the candidate list the user explicitly asked
  for and returns an auto proposal.
- The `"cascade"` action token in manifests is dead vocabulary — not in
  `IdentifyAction`, gated nowhere; cascade is driven solely by
  `EntityKindRegistry.EnumeratesIdentifyChildren`. Either make it a real action or
  remove it from all manifests.
- Transient identify of a large artist (203 albums) runs the full cascade
  synchronously in-request (~7 min, no progress). Steer big cascades to the queue.
- Episode stills overwrite the scan-generated `thumbnail` entity-file path (still →
  Thumbnail in `ImageKindRoleResolver`), orphaning the generated file. Product
  decision: dedicated `still` role or map to backdrop.
- `ImageCandidate.Rank/Language/Width/Height` are transient (UI ordering only);
  only one image per role survives.

### Contract drift (plugin-side vendored contracts)
`PluginContracts.cs` in the plugins repo lags the backend: `EntityMetadataPatch`
lacks `Rating`/`Flags` (TMDB can never emit a rating), `EntitySearchCandidate` lacks
`Popularity`. The vendored contracts should be generated/synced from
`Prismedia.Contracts/Plugins`.

---

## 2. What works well (verified end-to-end)

- External-id-first, parent-scoped entity resolution: **zero duplicate entities**
  across a full TMDB series cascade, a 20-volume MangaDex cascade, and a 203-album
  YouTube artist cascade (entity count unchanged; all children bound via
  `targetEntityId`).
- Relationship enrichment is genuinely good: cast/guest people got bios, tmdb+imdb
  ids, birth dates, classifications, and headshot files on disk; studios got logos,
  channel urls, descriptions.
- Artwork pipeline verified to disk: `/assets/plugins/artwork/<entity>/<role>-<hash>`
  with correct `entity_files` rows (role, `source='custom'`, mime). Per-volume manga
  covers landed on all 20 volumes.
- Proposed children with no local entity are correctly *not* created (track listings
  don't backfill missing files).
- MusicBrainz rate-limit discipline (cross-process 1.1 s pacing, UA, retries) held
  across ~80 paced calls; book-page subtrees are correctly inert.

---

## 3. Per-plugin findings

### TMDB (movie / series / season / episode / person / studio)
Live results: movie + series cascade matched and applied cleanly (8 episodes bound
to the correct local videos, positions normalized, guests enriched).

Plugin drops — fetched or trivially fetchable, currently lost:
- **No IMDb ids anywhere**: movie `imdb_id` is in the base response (record lacks
  the field); TV/season/episode need `append_to_response=external_ids` (also gets
  TVDB) — never requested.
- `created_by` (series creators) not deserialized; crew kept only for
  Director/Writer — producers, composers, DPs, editors dropped.
- Only the first production company; networks preferred over production companies
  (the other is dropped entirely).
- Person: `place_of_birth` deserialized but never mapped; `gender`,
  `also_known_as`, social external ids never requested.
- Dead appends: season `images` and episode `credits` are requested but deserialize
  into records lacking the properties (wasted fetch; season poster choices and
  episode main cast dropped).
- Never requested: `tagline`, keywords (→ tags), certifications/content ratings,
  `belongs_to_collection`, videos/trailers, `homepage`, budget/revenue,
  `original_title`/`original_language`, search `year` param.
- Search scoring is token-Jaccard with no year/popularity tiebreak — "Friendship"
  (2010) outranked the correct 2025 film; both score 1.0.
- `vote_average` unusable: patch `Rating` is int 0-5 and the vendored contract
  lacks the field anyway.
- Studio identify can't reuse `tmdbNetwork` hints (only reads key `tmdb`);
  network vs company are different TMDB namespaces and diverge silently.

### YouTube (video / music artist / album / track)
Live results: bracketed `[videoId]` extraction works (plugin-side, from
`Hints.FilePath`/`Entity.Title`); url lookup identical; artist cascade matched
203/203 albums and 203/203 tracks; studio (channel) entities created with logos and
banners.

Plugin drops (verified against raw InnerTube captures):
- `likeCount` sits in the already-deserialized microformat — dropped.
- `isFamilySafe` (→ `flags.isNsfw`), `isUnlisted`, premiere/live timestamps,
  `ownerProfileUrl` — all in microformat, all dropped.
- **Chapters**: 12 parseable chapters in the test video; no patch field exists
  (`entity_markers` fits exactly — see model gaps).
- Channel subscriber count fetched for studios but not emitted; music-artist path
  never reuses `FetchChannelAsync` (no banner, no description, no subscriber count
  for artists — only the square avatar).
- Real-album track children read only `flexColumns` → no durations (the
  search-matched singles *do* get `runtimeSeconds`); header song count / total
  runtime dropped.
- Search candidates emit title+thumbnail only; channel, views, length, published
  are present upstream and dropped (hurts disambiguation).
- oEmbed fallback drops `author_url` → no channel link on that path.
- `viewCount` parses as int32 — silently dropped over ~2.1B (and the contract +
  `entity_stats.value integer` couldn't carry it anyway).

### MusicBrainz (artist / album / track)
Live results: artist matched 1st-candidate with rich url-rels (60 urls), members as
credits with instrument roles; album track children bound by position and enriched.

Bugs:
- `RecordingProposalAsync` passes the *release title* as patch `Studio` → created a
  bogus "Summer Songs" studio. Should be the label.
- Standalone recording lookups pick the earliest Album-type release (often a
  compilation) instead of the ancestor album — wrong date/cover/label even though
  `AncestorMusicBrainzId` is available (used only in the search path).
- Album edition selection ignores local track count (picked the 11-track standard
  edition for a 14-track deluxe library album).
- Empty-string disambiguation sent as `description: ""` — on the Replace path this
  deletes an existing description.

Drops (all carriable in today's patch schema): artist founded/dissolved life-span →
`Dates` (currently prose-only); MB community ratings (0-5, fits `Rating`); release-
group genres (release-level genres are empty on MB — every album applied with zero
tags); release-group first-release-date; secondary types (Live/Compilation);
catalog number + barcode → `ExternalIds`; ISRCs; work relationships (composer/
lyricist) → `Credits`; per-track artist credits on children (wrong for VA
compilations); trackCount → `Stats`. CAA Front/Back/Booklet all flatten to `cover`.
Artist images come only from MB `image` url-rels (last.fm/Wikimedia) — consider
fanart.tv/TheAudioDB (see §5).

Latency: album lookup is 9 s, but once tracks have stored ids the host re-invokes
the plugin per child (≥2 paced MB calls each) → 44-55 s per album, ~54 s per
artist. Fine for the queue, sluggish for the interactive dialog; dominant cost is
host-side per-child re-identify.

### MangaDex (book / volume / chapter)
Live results: search relevance good; 20/20 volumes correctly matched with
per-volume covers; author/artist credits reused existing person rows; NSFW flag set
from content rating; oneshot chapters correctly bound.

**Critical — cross-volume chapter hijacking (corrupted the live library; repaired
2026-06-09 from archive filenames).** Upstream TPN now serves only 3 MangaPlus stub
chapters (`pages: 0`, `externalUrl` set, 992 chapters unavailable). Two compounding
bugs bound those 3 stubs into *every* volume:
- Plugin `MatchesChapterRequest` matches `requestSort+1 == chapterNumber` with no
  volume scoping, so any volume's first chapters match global chapters 1-3; a
  title-equality fallback then re-matches previously-corrupted titles, shifting the
  damage on every rerun.
- Stub chapters aren't filtered (`externalUrl`/`isUnavailable`/`pages:0` not even
  deserialized).
Result before repair: 80 chapters renamed, wrong `mangadexChapter` ids/positions/
dates, duplicate sort orders. Fixes: volume-scoped chapter matching, stub
filtering, and the backend merge dedup (§1 P1).
- `ScopedFallback` leaks book external ids/urls/dates onto unmatched chapters and
  reports `confidence 0.9 / external-id` for a non-match.
- Doujin tag-table descriptions: the `||TAGS|` format isn't stripped/parsed —
  raw markdown leaks into descriptions.

Drops (mostly carriable today): the `links` object isn't even deserialized —
AniList/MAL/Kitsu/MangaUpdates ids and BookWalker/Viz/raw urls all lost (this is
the cross-provider bridge for anime↔manga); `/statistics` (bayesian rating,
follows) never fetched; `lastVolume`/`lastChapter` → stats; `status` only becomes a
tag, `classification` left null; author-vs-artist flattened to `creator`;
tag groups (genre/theme/format) flattened; cover urls always the 512px thumbnail;
`contentRating` safe/suggestive distinction dropped; related manga (sequel/
spin-off) unfetchable into the model (see gaps).

### AniList (tested 2026-06-10)
Live results: the per-season Media model maps correctly — the plugin BFS-walks
PREQUEL/SEQUEL relations, date-orders the parts, and binds local season N to the
Nth part (local Season 3 → "Culling Game Part 1", anilist 172463, correct dates and
episode count). Series carries `anilist`+`mal` ids; season/episode get anilist ids
for round-trip lookup-id; banner + poster artwork verified on disk; zero duplicate
entities; the backend P0 fixes were independently confirmed during the apply.

Bugs:
- **Placeholder episode titles clobber real ones**: the plugin emits `"Episode N"`
  for episodes, and cascade children apply every patch field — a real episode title
  was renamed to "Episode 11" (restored manually). Fix: don't emit placeholder
  titles, or fetch `streamingEpisodes` for real ones.
- Series node is just AniList's season-1 media — series-level dates/stats describe
  only season 1 (episodeCount degraded 59→24). Should aggregate the parts.
- Characters are mis-modeled as person *relationship* proposals with empty root
  credits → zero cast links persist. Voice actors/staff should ride the root
  `Credits` (and "character" isn't in the `CreditRole` code set).
- Producer studios ride as relationship nodes but only `patch.Studio` makes a link
  — effectively dropped.
- No rate-limit handling at all (no 429/Retry-After, no pacing); a 10-season series
  cascade ≈ 100 uncached GraphQL calls vs AniList's 90/min. Same fix as MangaDex
  1.1.4 applies. One failed child identify silently drops that child.

Drops (queried but discarded at deserialization): `averageScore` (→ Rating),
`isAdult` (→ NSFW flag), `status` (no free Classification slot), `season`/
`seasonYear`, `coverImage.color`. Never queried: synonyms (no alias home), staff,
voice actors, `streamingEpisodes` (episode titles + thumbnails), `airingSchedule`
(per-episode air dates), `externalLinks`, trailer, tag `isAdult`/`isSpoiler` flags
(spoiler tags can leak into the library), `source`, `countryOfOrigin`. The plugin
also pads fuzzy dates to full days (fabricated precision) — the backend now accepts
`"2026"`/`"2026-01"` raw. Date-code vocabulary split: AniList writes
`started`/`ended` next to TMDB's `firstAir`/`lastAir` with no normalization.
Neither `anilist` nor `mal` are in the known-provider constants/codegen.

---

## 4. Entity-model / contract gaps (no home in today's patch schema)

Ordered roughly by payoff:
1. **Aliases / alternate titles** — person `also_known_as`, manga `altTitles` per
   language, movie `original_title`, MB artist aliases. No patch field, no table.
2. **Markers/chapters in the patch** — `entity_markers` (title/seconds/end) already
   exists and fits YouTube chapters exactly; `EntityMetadataPatch` can't carry them.
3. **Partial-precision dates** — `entity_dates.precision` column exists but is
   unused by the validator/apply path (see P0-3).
4. **Classification is a single overloaded string** — TV status, person department,
   studio origin country, album type, and (if added) certifications all compete for
   one slot. Make it multi-code (the table already supports per-code rows).
5. **Stats are int32 end-to-end** — view counts overflow; budget/revenue wouldn't
   fit either. Widen to bigint/long.
6. **Rating precision** — patch `Rating` is int 0-5; TMDB 0-10 decimals and MB
   5-star decimals lose precision.
7. **Entity↔entity relations beyond credits/studio/tags** — no proposal kind for
   collection membership (TMDB `belongs_to_collection`), book↔book (sequel/
   spin-off), series↔series (AniList relations), studio parent/hierarchy.
8. **Image roles** — back cover/booklet/still/banner have no distinct role;
   everything flattens to cover/poster/backdrop/thumbnail and one file per role.
9. **Per-chapter credit scope** — `book-chapter` isn't a relationship-owner kind,
   so scanlation groups hoist to the book (last-writer-wins as book `Studio`).
10. **Credit date ranges** — band member tenures (begin/end) have no slot.
11. **Tagline** — no field; Description is taken.
12. **Trailers/videos** — artwork model is images-only.

---

## 5. Candidate new plugin sources

Kinds with **zero provider coverage today**: novels/non-manga books (the ASoIaF
library has no possible provider), western comics, galleries, images.

- **OpenLibrary / Google Books** — novels: covers, descriptions, authors, ISBNs,
  series. No auth (OpenLibrary). Highest-value new plugin; the library already has
  the data starving for it.
- **ComicVine (or Metron)** — western comics/issues. API key.
- **TheAudioDB / fanart.tv** — artist headshots, banners, logos; album art beyond
  CAA. Fills MusicBrainz's no-images hole.
- **AniDB / Kitsu** — anime alternates to AniList (notable given today's outage);
  MangaDex's `links` object provides the ids for free once deserialized.
- **TVDB** — TV alternate + the ids TMDB's `external_ids` append exposes.
- **Trakt / OMDb** — ratings aggregation if rating precision lands.
- **StashDB / stash-compat scrapers** — the scraper endpoint already exists
  (`/api/plugins/stash-scrapers`); relevant for NSFW gallery/performer saturation.
- **Audnexus/Audible** — audiobooks, if that kind activates.

---

## 6. Recommended fix order

1. Backend P0s: EF Replace/Upsert deletion bug; route transient apply through
   `ApplyAsync`; date validation + precision (root and children identical).
2. MangaDex chapter matching (volume scoping + stub filtering) + backend merge
   dedup + sort-order uniqueness — the only actively-corrupting combination.
3. Contract sync for the plugins repo (Rating/Flags/Popularity), then cheap
   high-value saturation: TMDB `external_ids` append + `created_by` + keywords +
   person fields; MangaDex `links` + statistics; YouTube likes/family-safe/banner +
   album track durations; MusicBrainz label-as-studio fix + ratings + RG genres +
   first-release-date.
4. Model work by payoff: aliases, markers-in-patch, multi-code classification,
   bigint stats, relation kinds, image roles.
5. New sources: OpenLibrary first (unserved kind with real library data), then
   TheAudioDB/fanart.tv, ComicVine.

Test artifacts from this audit (proposal/apply JSON, raw upstream captures) were
saved under `/tmp/{tmdb,youtube,musicbrainz,mangadex}-*.json` on the dev machine.
