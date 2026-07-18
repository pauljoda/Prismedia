# Library Scan Performance Baseline — 2026-07-18

## Scope and safety

This audit measures the video `scan-library` path before changing its behavior. Root labels,
paths, entity IDs, and media metadata are intentionally omitted.

- Instrumented runtime commit: `0b2864b7`
- Native test image: `linux/amd64`, image
  `sha256:4209c9d7c87d92725bfe5ebbe4e8478377cb587cc40355533cfac9db925fb1a2`
- Test target: the development-only `prismedia-dev` container on the rich-data host
- A verified development database snapshot was taken before deployment.
- The standard live container was queried only through read-only transactions and container
  inspection. It was not deployed to, restarted, reconfigured, or used to run test jobs.
- The full test began with no video scan snapshot because the pending subtitle-sidecar migration
  intentionally invalidated video snapshots. Existing entities and media were retained.

Two runtime commits landed in the shared checkout while the long baseline was running. Their diffs
affect imported-episode auto-identify/job targeting and AAC remux playback; they do not touch
snapshot gating, sidecar application, scan batch persistence, wanted binding, or discovery. The
measurements remain explicitly tied to `0b2864b7`. After the audit, the resulting current runtime
HEAD (`dd5e1952`) was built natively and deployed to `prismedia-dev` as
`sha256:e73375d4cf18bed92caeb0059d6bce91aac04f62197c48bf38573adc562668fa`; internal and public
health checks returned HTTP 200 with all job lanes idle.

## Results

| Workload | Scope | Result |
| --- | --- | ---: |
| Controlled full pass | 9,176 videos / 9,699 video-and-subtitle signatures | 3,443.25 s (57m 23s) |
| Controlled unchanged pass | 9,742 signatures | 6.28 s |
| Live historical `scan-library`, all completed | 303 jobs | median 3.9 s; p95 3,056.3 s; max 3,452.4 s |
| Live historical long scans | 30 jobs between 45–59 minutes | mean 3,108.8 s; median 3,057.0 s |

The live history is strongly bimodal: 268 scans finished in under ten seconds at a 4.6-second
average, while 30 scans took 49m21s–57m32s. During the audit, a current live scan reported three
changed roots with `+206`, `+49`, and `+6` additions; each changed root still fell through to its
complete detailed reconciliation.

Other live scan types are not responsible for the recurring half-hour-plus runtime:

| Job type | Completed | Mean | Maximum |
| --- | ---: | ---: | ---: |
| `scan-audio` | 334 | 3.7 s | 160.6 s |
| `scan-book` | 335 | 0.5 s | 19.1 s |
| `scan-gallery` | 333 | 0.7 s | 26.6 s |

## Full-pass phase breakdown

The detailed video reconciliation took 3,439.65 seconds. The top three phases account for 98.1%
of that time.

| Phase | Time | Share of detailed pass |
| --- | ---: | ---: |
| Apply sidecar metadata | 1,700.71 s | 49.4% |
| Persist video/hierarchy batches | 1,293.98 s | 37.6% |
| Bind wanted/acquisition entities | 378.85 s | 11.0% |
| Write progress | 26.08 s | 0.8% |
| Enqueue downstream jobs | 21.89 s | 0.6% |
| Read sidecars | 8.64 s | 0.3% |
| Check downstream needs | 6.90 s | 0.2% |
| Discover 9,176 videos | 0.55 s | <0.1% |

Root-level work outside the detailed pass was also small: signature enumeration took 1.20 seconds,
snapshot load and diff took 0.03 seconds, and snapshot application took 1.24 seconds.

The unchanged pass confirms the fast gate is inexpensive:

| Unchanged phase | Time |
| --- | ---: |
| Enumerate 9,742 signatures | 0.94 s |
| Load snapshot | 0.03 s |
| Diff snapshot | 0.01 s |
| Plan recovery jobs | 4.96 s |
| Complete job | 6.28 s |

The rich library changed while the unchanged test was being prepared. A first attempt detected 43
new videos and was cancelled before progress began. For the controlled measurement only, those
current signatures were temporarily added to the development snapshot. After the measurement the
adjustment was reversed: all 43 videos remain absent from the snapshot and discoverable by the next
real development scan. No media files were changed.

## Evidence and causes

### 1. Any change expands into a complete root reconciliation

`ScanJobHandler` cheaply discovers and diffs signatures, but any add, remove, or modification calls
`ScanRootCoreAsync` for the complete root. Discovery is only 0.55 seconds; the resulting full
materialization is 57 minutes. This is the largest structural multiplier and explains the live
bimodal history.

The classification requirement does not require repeating every database write. The scanner can
still discover/classify complete folder context, then persist only the delta and hierarchy affected
by changed directories.

### 2. Sidecar metadata is saved one video at a time through an ever-growing EF graph

9,175 of the 9,176 baseline videos had an adjacent NFO sidecar. Reading all sidecars took only 8.64
seconds. Applying them took 28m21s.

For every sidecar, `ApplyVideoSidecarMetadataAsync` performs several reads and calls
`SaveChangesWithLifecycleAsync`. The same scoped `PrismediaDbContext` is retained for the whole job.
Tracked rows therefore accumulate across all batches. Hot helpers scan `DbSet.Local`, and the save
path calls change detection repeatedly over the growing graph. It also writes `entity.UpdatedAt`
and saves even when the persisted metadata is already complete.

### 3. Batched video persistence has the same unbounded tracking cost

The nominal batch size is 50, but batches share the job-wide context. Throughput fell from roughly
400 files/minute early in the pass to about 100–150 files/minute late in the pass while worker CPU
remained near one core and database command latency stayed broadly stable. Worker RSS grew from
roughly 276 MiB to 322 MiB. The 21m34s persistence phase and nonlinear slowdown are consistent with
repeated `DbSet.Local` scans and change detection over retained tracked entities.

### 4. Wanted binding performs unneeded, unindexed source-path queries

There were no unconsumed acquisition import hints, but every episodic item still ran wanted-parent,
wanted-child, and wanted-leaf checks. `BindWantedChildBySortOrderAsync` resolves the parent with:

```sql
WHERE role = 'source' AND path = $1
```

`entity_files` has a primary-key index and a unique `(entity_id, role)` index, but no path index. A
representative cached query used a parallel sequential scan, removed about 164,000 rows, touched
3,897 shared buffers, and took 32.85 ms.

Before downstream jobs began, the scan logged 285,063 database commands (about 31 per video) and
504.78 seconds of database execution time. The first-table command profile included:

| First table | Commands | Database time |
| --- | ---: | ---: |
| `entity_files` | 18,895 | 333.98 s |
| `entities` | 87,430 | 49.46 s |
| `entity_relationship_links` | 23,502 | 20.65 s |
| `entity_positions` | 18,444 | 17.80 s |
| `acquisition_import_hints` | 18,352 | 8.93 s |

`entity_files` alone consumed 66.2% of recorded database execution time. By completion it had also
recorded approximately 4.97 billion sequential tuple reads since the development database process
started.

### 5. The unified runtime does not load its packaged logging settings

The unified image starts both .NET processes with content root `/app`, while their settings files
are under `/app/api` and `/app/worker`. The intended EF command threshold is therefore not loaded,
and successful commands are logged at Information. The controlled scan emitted about 119 MB of log
output before downstream execution began. This is not the primary 57-minute cost, but it adds CPU,
I/O, and substantial diagnostic noise.

## Recommended experiment order

No optimization was applied during this baseline. The next implementation work should be measured
against the same phase metrics in this order:

1. Reconcile the signature delta instead of rematerializing every video. Preserve complete
   discovery/classification context, but persist only added/changed files, removals, and affected
   hierarchy.
2. Bound EF tracking per batch and batch sidecar metadata application. Avoid per-video saves and
   skip metadata writes when no persisted value changes.
3. Add a non-unique `(role, path)` lookup index and add a scan-level no-hints fast path or preload
   wanted bindings in batches.
4. Start the unified API/worker with their own content roots (or copy settings to the actual content
   root) so production logging configuration is honored.

Each change should be deployed as a commit-tagged native `linux/amd64` image to `prismedia-dev`, run
against the same rich workload, and compared using full-pass, changed-pass, and unchanged-pass
timings. The standard live container remains read-only for historical comparison.
