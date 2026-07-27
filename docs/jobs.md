# Durable Jobs

Prismedia persists long-running work as dependency graphs in PostgreSQL. A graph is both the unit of progress/cancellation and a logical execution lane; it is not an operating-system thread or a permanently reserved worker.

## Scheduling pools

- Every top-level interactive Entity action creates a distinct interactive graph. A bulk action creates one graph for each selected Entity.
- At most one node from an interactive graph runs at once. Runnable graphs are selected by least-recent dispatch, so child-heavy Entity trees cannot monopolize the foreground.
- The interactive execution limit is `clamp(ceil(logical processors / 2), 1, 4)` and has no user setting.
- Scans, backups, sweeps, collection refreshes, maintenance, monitoring, and scheduled acquisitions use the background pool. `jobs.backgroundConcurrency` is the live upper bound on executing background handlers.
- A child node inherits its graph, origin, initiating user, lane, and top-level target. Handlers cannot redirect child work into another graph.

## Graph records

`JobGraph` stores origin, top-level target, status, cancellation, timestamps, and an optional active singleton key. `JobRun` is one executable graph node with a stable graph-local key, sequence, importance, retry time, resource profile, and optional parent. `JobDependency` stores required predecessor edges. `JobGraphSignal` stores waits such as Identify review or an external transfer without occupying a worker.

Stable node keys make dynamic expansion idempotent. A retry may append the same intended child again, but the graph-local unique constraint returns the existing node. Background singleton work uses active graph keys; separate interactive actions are never deduplicated together.

Required-node failure skips dependent descendants and fails the graph. Best-effort failure is retained as a warning, while independent branches may continue. Cancelling a graph cancels queued nodes and open signals, signals running handlers, and invokes linked acquisition cancellation without deleting already imported media.

## Resource scheduling

The central job definition registry classifies work as light, standard CPU, or heavy CPU. The process shares `max(1, logical processors - 1)` CPU permits across both scheduling pools. Standard work costs one permit; heavy work costs two when available. Interactive CPU work receives the next free permit before new background CPU work, but running work is not preempted.

Entity mutation resources (`entity:{id}`) serialize conflicting writes across otherwise independent graphs. Graph persistence declares these implicit resources before a node becomes runnable, and worker recovery repairs declarations missing from older queued nodes. Plugin manifests may declare maximum concurrency and a minimum start interval; the host turns those declarations into durable `plugin:{id}` resources shared by interactive and background graphs. Built-in acquisition adapters use the same opt-in policy contract: Prowlarr retains two concurrent searches, slskd retains one, and direct Torznab/Newznab adapters receive no invented limit. Resource capacity is checked before a node is claimed, and leases follow node heartbeats so a crashed worker cannot permanently hold capacity.

Graph projection changes and dynamic expansion are serialized per graph. Routine progress heartbeats update the node without rewriting the shared graph row; terminal transitions reconcile the projection under the graph lock. Startup recovery reconciles stale nodes and inconsistent active graphs with no remaining work or open signal, so a graph cannot remain indefinitely running after all of its nodes have already reached terminal states.

## Identify and acquisition waits

Identify search, one-provider-per-Entity expansion, reviewed proposal application, and reconciliation stay in one interactive graph. Candidate review is an Identify signal. Accepting the proposal closes that signal and appends the required apply node transactionally.

An acquisition graph may pause for release review and for its download client. Submission opens an external-transfer signal. The background acquisition monitor closes it after completion or failure and appends import/recovery work to the original graph. Scheduled monitored acquisitions remain background graphs; pre-graph orphaned acquisitions receive background recovery graphs.

## Entity-scoped imports

All acquisition import engines materialize through the imported-Entity facade. The result names canonical imported Entities, affected ancestors, exact added/replaced/removed paths, and source revision/file-role state. Materialization updates scan snapshots for those exact paths.

The `reconcile-entity` planner derives downstream work from Entity kind, capabilities, owned files, source revision, and asset state. It never enumerates a library root. Required probes gate `acquisition-finalize`; fingerprints, previews, artwork variants, subtitles, and other optional enrichment are best-effort. Ancestor projections run after affected child readiness. Scheduled or explicitly requested administrative scans remain the mechanism for discovering unrelated out-of-band filesystem changes.

## API and operations UI

Graph endpoints list aggregate lane progress and expose nodes, edges, signals, warnings, wait reasons, and cancellation. Durable foreground endpoints return or expose their graph IDs; administrative operations return background graph references. The flat job history endpoints remain a compatibility and diagnostic projection.

The Jobs page renders executing graphs as active lanes, while graphs paused on durable signals appear in a separate waiting-workflows section because they consume no worker or scheduling capacity. Each workflow expands into its dependency tree. Provider waits use friendly labels, and Entity-backed graph data obeys the active NSFW visibility policy.
