# pHash Contribution Pipeline

Prismedia identifies scenes against [StashDB](https://stashdb.org), ThePornDB, and
other StashBox-protocol servers using perceptual hashes (pHash), along with MD5
and OpenSubtitles (oshash) file hashes. This document explains how hashes are
generated, how contribution works, and why we share Stash's exact pipeline.

## Why contribute fingerprints?

`submitFingerprint` is **not** a metadata edit channel — StashDB has a separate
scene-edit/vote system for that. It does exactly one thing: associates a local
file hash with a known remote scene ID so the community index grows.

The value of that is:

1. **Coverage for re-encodes.** `md5` and `oshash` are byte-exact — any
   re-encode, container swap, or trim breaks them. `phash` is perceptual — it
   survives recompression, resolution changes, and minor trims. StashDB's phash
   index is only as good as what people contribute. When you submit your copy's
   phash, the next user with a different re-encode of the same scene gets an
   automatic match instead of having to search by title.
2. **Strengthening weak matches.** If you identified a scene via title search
   (not fingerprint), StashDB has zero fingerprints linking your copy to that
   scene. Submitting turns "had to search by title" into "auto-matches next
   time" for everyone with a similar file.

## Pipeline

### Perceptual hash generation

The worker computes pHashes via the bundled `prismedia-phash` Go helper. The
pipeline matches Stash's `pkg/hash/videophash/phash.go` step-for-step — any
divergence would produce hashes that do not cluster with existing community
fingerprints.

```
1. For i in [0, 25):
     time = 0.05*duration + i * (0.9*duration/25)
   Frame 0 lands at 5% of duration, frame 24 lands at 91.4%.

2. ffmpeg -ss <time> -i <file> -frames:v 1 \
          -vf scale=160:-2 -c:v bmp -f rawvideo -
   (Input seek — `-ss` BEFORE `-i` — is load-bearing. Output seek
    picks different frames and breaks compatibility.)

3. Paste all 25 frames into a 5×5 NRGBA montage via
   github.com/disintegration/imaging. Canvas size is (W*5) × (H*5)
   from the first decoded frame — a 16:9 source yields an 800×450
   montage, NOT 800×800. Stash preserves aspect ratio; so do we.

4. goimagehash.PerceptionHash(montage) → uint64

5. Format as a lowercase 16-character hex string.
```

Source: `infra/phash/main.go`. Built statically in a `golang:1.23-alpine`
stage inside `infra/docker/unified.Dockerfile` and `infra/docker/worker.Dockerfile`,
copied into the runtime image at `/usr/local/bin/prismedia-phash`, with
`PRISMEDIA_PHASH_BIN` pre-set. The worker's `computePhash` helper in
`@prismedia/media-core` shells out to it with `-file` and `-duration`, validates
the 16-char hex output, and returns `null` on `ENOENT` so dev machines without
the binary keep running.

### Worker integration

The .NET fingerprint job reads the
`library_settings.generate_phash` flag. When enabled and the scene has a known
duration, it runs `computePhash(filePath, duration)` after md5/oshash and
writes the result to `scenes.phash`.

The same processor accepts a `phashOnly: true` flag on the job payload. When
set, it skips md5/oshash and only refreshes the phash — which is how
`POST /jobs/phash-backfill` reuses the existing queue to populate hashes for
every scene that has a duration but no stored phash.

### Contribution flow

```
 Identify → Accept → Auto-link → Submit
  (scene)   (apply   (stash_ids) (fingerprint
             metadata)             _submissions)
```

1. **Identify**: `POST /stashbox-endpoints/:id/identify` checks for an
   existing `stash_ids` row for the target endpoint first (short-circuit by
   known remote ID). If none exists, it falls through to the fingerprint
   cascade (oshash → md5 → phash) and then title search. The result lands in
   `scrape_results` with `matchType: "stashid" | "fingerprint" | "title"`.
2. **Accept**: `POST /scrapers/results/:id/accept` applies the proposed
   metadata inside a transaction. When the result came from a StashBox endpoint,
   it also inserts a `stash_ids` row linking the local scene to its remote
   scene ID (`onConflictDoNothing`). This is what makes the scene contributable.
3. **Contribute**: `POST /stashbox-endpoints/:id/submit-fingerprints` loads the
   scene and its linked `stash_ids` row, then calls `submitFingerprint` once
   per present algorithm (md5/oshash/phash). Refuses with 400 if `duration <= 0`
   — Stash does the same. Each call is recorded in `fingerprint_submissions`,
   upserted on the `(scene, endpoint, algorithm, hash)` unique index.
4. **Bulk contribution**: The web pHashes tab iterates over `(scene, endpoint)`
   pairs sequentially so the per-endpoint rate limiter holds.

### GraphQL mutation

Matches upstream stash-box schema exactly:

```graphql
mutation SubmitFingerprint($input: FingerprintSubmission!) {
  submitFingerprint(input: $input)
}

input FingerprintSubmission {
  scene_id: ID!
  fingerprint: FingerprintInput!
  unmatch: Boolean
}

input FingerprintInput {
  hash: String!
  algorithm: FingerprintAlgorithm!  # MD5 | OSHASH | PHASH
  duration: Int!                    # seconds, rounded
}
```

The client lives in `packages/stash-compat/src/stashbox/client.ts`. Duration
is rounded with `Math.round(scene.duration)` at call time and clamped to a
minimum of 1 — StashBox rejects zero.

## Rate limiting

Every `StashBoxClient` instance has an internal token bucket at 240 requests
per minute (Stash's default). Previously, each API route handler `new`-ed a
fresh client per request, so the bucket was effectively reset on every call
and bulk operations could blow right through the limit.

The fix lives in the .NET StashBox runtime: a process-wide
Map keyed by endpoint UUID that hands out a cached client via
`getStashBoxClient(ep)`. Rate limiting now holds across concurrent requests
and across the lifetime of the .NET API process. PATCH/DELETE handlers
call `invalidateStashBoxClient(id)` so credential changes take effect
immediately.

## Troubleshooting

### `prismedia-phash` is not on PATH

The worker logs a warning and skips phash generation gracefully. You can:

- Use the unified Docker image, which bundles the binary at
  `/usr/local/bin/prismedia-phash`.
- Build it locally: `cd infra/phash && go mod tidy && go build -o prismedia-phash .`
  then put the binary somewhere on `$PATH` or point `$PRISMEDIA_PHASH_BIN` at it.

### StashDB returns false from `submitFingerprint`

The API response records `status: "error"` with the endpoint's message in
`fingerprint_submissions.error`. Common causes:

- The remote scene ID is stale (scene was merged or deleted upstream).
  Re-run identify — the short-circuit will fall through to the cascade.
- The duration in the payload disagrees with what StashDB has by more than a
  few seconds. Re-probe the scene to refresh `scenes.duration`.
- API key does not have contribute permission on the target endpoint.

### "Scene is not linked" (404 on submit-fingerprints)

The scene has no `stash_ids` row for the target endpoint. Either:

- Run `Identify` and accept a match (auto-creates the link), or
- Open the pHashes tab and use the "Add link" chip to paste the remote
  scene UUID manually.

### Hashes don't match StashDB's index

`pkg/hash/videophash/phash.go` is the source of truth. If you suspect
Prismedia's output has drifted, diff `infra/phash/main.go` against it — every
constant (`screenshotWidth=160`, `columns=5`, `rows=5`, step formula, ffmpeg
seek-order, scale filter, montage library, hash algorithm) is load-bearing.
Do not "clean up" any of them.

## References

- Stash source: `pkg/hash/videophash/phash.go`, `pkg/stashbox/scene.go`
- StashDB guidelines: <https://guidelines.stashdb.org/docs/faq_getting-started/stashdb/whats-a-phash/>
- StashDB contribution guide: <https://guidelines.stashdb.org/docs/faq_getting-started/stashdb/contributing-to-stashdb/>
- Hacker Factor perceptual hashing primer: <https://hackerfactor.com/blog/index.php%3F/archives/432-Looks-Like-It.html>
