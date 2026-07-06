# Acquisition E2E Verification — 2026-07-04

Live gate for the acquisition parity work (task #18), run against the production
Prowlarr on `10.10.10.100` and the dev Prismedia stack on this machine.

## What was verified live (non-destructive)

| Check | Result |
|---|---|
| Prismedia → live Prowlarr connectivity | Connected (`prowlarr.pauljoda.com`, 32 indexers: 30 torrent + 2 usenet). |
| Search reproduces the live release universe | A book search ("Dune") through Prismedia's Prowlarr indexer returned **140 candidates, 77 accepted**, all correctly book-attributed. Direct Prowlarr for the same categories returned 221 (broader bare-query); Prismedia's lineage ladder ("Dune Frank Herbert") legitimately narrows it. |
| **Usenet end-to-end (P0)** | Prismedia's live candidates included **16 usenet** alongside 124 torrent; **12 usenet accepted, 0 rejected as wrong-protocol** — proving the protocol gate accepts usenet *because* SABnzbd is configured, against the real DrunkenSlug/AnimeTosho indexers. |
| Category-8000 fix | Exercised implicitly — the default `7000,8000` book config surfaced Other-range results that the pre-fix code dropped. |
| SABnzbd client auth | Connected to SABnzbd 5.0.4 (dev container, `localhost:8090`). |
| qBittorrent client auth | Connects with the full stored credentials (`admin` + stored password) against qBittorrent 5.2.2 (`localhost:8080`). |
| Decision ranking | Deterministic, accepted-first, ordered by score; usenet (no seeders) ranks on quality/preference, not seeders. |

Notes from the run, for future reference:
- qBittorrent 5.2.2 returns **HTTP 204 + Set-Cookie** on a successful login (not the legacy `Ok.` body); Prismedia's cookie extraction handles it.
- The download-client **Test** endpoint reuses the stored *password* but takes the *username* from the request — the UI always sends the username, and the real grab path builds its connection from the full stored client, so this is only a headless-curl artifact, not a product gap.
- The dev qBittorrent's stored password in Prismedia was stale; it was corrected to `admin`/`prismedia-dev`.

## The remaining step — the live grab (to run through the UI)

Everything up to the grab is proven; the actual grab→download→import pulls real
content and uses indexer/usenet quota, so it's left to run by hand. Checklist:

1. **Torrent path** — Request a small, freely-distributable item (e.g. a public-domain
   book, or point a search at a Linux-ISO-style torrent). Pick a torrent release; confirm:
   - it appears in qBittorrent under the `prismedia` category,
   - on completion it **hardlinks** into the library (set the profile import mode to
     Hardlink) — verify the download copy still exists and the library file shares its inode
     (`ls -i` on both paths),
   - the scan binds it to the wanted entity (no duplicate),
   - a `grabbed` then `imported` row appears in the acquisition's History section,
   - with a seed goal set on the indexer, the torrent keeps seeding after import and is
     removed once the goal is met (the seeding watch).
2. **Usenet path** — Request something available on usenet; pick a usenet release; confirm it
   routes to SABnzbd, tracks through queue→history, imports on `Completed`, and logs history.
3. **Failure paths** (optional) — a `.scr`-bearing release should hold at
   ManualImportRequired with a visible warning; a stalled/removed torrent should hit
   failed-download recovery and (with auto-redownload on) grab the next-best.

## Status

Tasks #1–#17 implemented, tested (Domain 104 / Application 248 / Infrastructure 1013 /
Api 257), and pushed to PR #44. Task #18 verified non-destructively as above; the live
grab is owned by the maintainer.

2026-07-06 quality pass: code/test gates remain green after the final audit, and the
live grab/import remains the release blocker. Before shipping, run one torrent
grab/import and one usenet grab/import through Prismedia at `http://localhost:8008`,
including category/routing, scan binding without duplicates, history entries,
cancel/remove behavior, and seed-goal cleanup.
