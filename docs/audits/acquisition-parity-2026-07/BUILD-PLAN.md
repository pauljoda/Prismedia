# Acquisition Parity Build Plan — 2026-07

Companion to [GAP-MATRIX.md](GAP-MATRIX.md), which audits Prismedia's acquisition system
(branch `feat/book-acquisition`) against Sonarr + Prowlarr source (`~/Dev/_ARCHIVE`) and the
live production instances. The reference reports in this directory document the Sonarr/Prowlarr
behavior each work item replicates; Prismedia-side evidence lives in the matrix itself.

## Scope decisions (2026-07-03)

1. **Indexers — native Torznab/Newznab, Prowlarr stays supported.** Prismedia gets first-class
   direct Torznab/Newznab indexer clients (caps detection, per-indexer priority/health/backoff,
   category handling) so it no longer *requires* Prowlarr; Prowlarr remains one indexer kind and
   Jackett works via Torznab. A native Cardigann YAML engine (true tracker-definition support) is
   explicitly deferred to a later branch.
2. **Branch scope — P0 + all P1.** Usenet/SABnzbd plus the full P1 cluster (dangerous-file hold,
   seed lifecycle, hardlink import, video/music owned-quality + upgrade-until-cutoff, naming
   templates, proper/repack, custom-format scoring with thresholds, remote path mapping, durable
   history, Missing/Cutoff-Unmet views, Transmission + client routing, monitor presets).
   P2 items (RSS sync, delay profiles, manual import UI, calendar, per-quality size gates,
   extra-file import) move to a follow-up branch.
3. **Recycle bin — opt-in, softening the hard-delete policy.** An off-by-default recycle bin
   (path + cleanup-days) for acquisition-managed file replacement/deletion, reconciled with the
   existing `.prismedia-bak` upgrade sidecar.

## Build order

Phased by dependency; each item lands with its EF migration, generated-code refresh, tests,
frontend surface, and changelog entry.

**Phase 1 — transport foundations**
1. Torznab category-8000 range fix (verified live bug)
2. Usenet protocol + SABnzbd client (the one P0 — the live install depends on usenet)
3. Download-client routing: protocol filter, priority/fallback, Transmission, per-kind category
4. Native Torznab/Newznab indexer clients + caps
5. Indexer reliability: consumed priority, escalating backoff, rate limits

**Phase 2 — quality & decision model**
6. Generalized quality model: per-kind quality catalog, orderable allowed list + groups, cutoff,
   `upgradeAllowed:false` pinning, owned quality, video/music upgrade loop
7. Proper/Repack/REAL revision parsing + release-group extraction + tri-state propers setting
8. Custom Formats: named scored classifiers, per-profile scores, min/cutoff score thresholds

**Phase 3 — import & file lifecycle**
9. Dangerous-extension import hold (live-verified scenario)
10. Remote path mapping
11. Hardlink import mode
12. Seed ratio/time lifecycle (defer delete until seed goal)
13. Opt-in recycle bin
14. Configurable naming templates for movie/TV/music

**Phase 4 — monitoring & surfaces**
15. Durable acquisition history / activity log
16. Missing + Cutoff-Unmet list views (live scale: ~4,900 missing episodes)
17. Monitor-type presets + shared Entity child-monitoring editor

**Final gate**
18. E2E verification: replicate the live Prowlarr/Sonarr search results and decision outcomes
    through Prismedia, then run the full hands-off loop per kind against dev qBittorrent + SABnzbd.

## Already at or beyond parity (do not regress)

Multi-kind acquisition through one loop; wanted items as real library entities hidden from
Jellyfin until fulfilled; container→child Entity materialization with shared monitoring; three-point blocklist enforcement
with content-addressed identity; crash-safe upgrade swap; per-piece torrent progress; entity-page
interactive release picker; careful stall heuristics. See GAP-MATRIX.md §4.
