<script lang="ts">
  import {
    AlertTriangle,
    ChevronDown,
    ChevronRight,
    CirclePause,
    Cpu,
    GitBranch,
    Loader2,
    Square,
  } from "@lucide/svelte";
  import { Badge, Button, StatusLed, cn } from "@prismedia/ui-svelte";
  import type {
    JobGraphDetailResponse,
    JobGraphNode,
    JobGraphSummary,
  } from "$lib/api/generated/model";
  import {
    JOB_GRAPH_ORIGIN,
    JOB_GRAPH_STATUS,
    JOB_NODE_IMPORTANCE,
    JOB_RESOURCE_CLASS,
    JOB_RUN_STATUS,
  } from "$lib/api/generated/codes";
  import { jobLabelForType } from "$lib/jobs/jobs-dashboard";
  import { formatRelativeTimeShort } from "$lib/jobs/helpers";

  interface Props {
    graph: JobGraphSummary;
    detail?: JobGraphDetailResponse | null;
    expanded: boolean;
    loadingDetail: boolean;
    cancelling: boolean;
    onToggle: (graph: JobGraphSummary) => void | Promise<void>;
    onCancel: (graph: JobGraphSummary) => void | Promise<void>;
  }

  let {
    graph,
    detail = null,
    expanded,
    loadingDetail,
    cancelling,
    onToggle,
    onCancel,
  }: Props = $props();

  const progress = $derived(Math.max(0, Math.min(100, Number(graph.progress))));
  const nodeCount = $derived(Number(graph.nodeCount));
  const terminalCount = $derived(Number(graph.terminalNodeCount));
  const failedCount = $derived(Number(graph.failedNodeCount));
  const warningCount = $derived(Number(graph.warningCount));
  const isCancellable = $derived(
    graph.status === JOB_GRAPH_STATUS.queued ||
      graph.status === JOB_GRAPH_STATUS.running ||
      graph.status === JOB_GRAPH_STATUS.waiting,
  );
  const openSignals = $derived(
    detail?.signals.filter((signal) => !signal.resolvedAt && !signal.cancelledAt) ?? [],
  );
  const orderedNodes = $derived.by(() => orderNodes(detail?.nodes ?? []));

  function statusTone(status: string): "idle" | "phosphor" | "warning" | "error" | "active" {
    if (status === JOB_GRAPH_STATUS.running) return "phosphor";
    if (status === JOB_GRAPH_STATUS.waiting || status === JOB_GRAPH_STATUS.completedWithWarnings) return "warning";
    if (status === JOB_GRAPH_STATUS.failed) return "error";
    if (status === JOB_GRAPH_STATUS.completed) return "active";
    return "idle";
  }

  function nodeTone(status: string): string {
    if (status === JOB_RUN_STATUS.running) return "text-text-accent";
    if (status === JOB_RUN_STATUS.failed) return "text-status-error-text";
    if (status === JOB_RUN_STATUS.completed) return "text-status-success-text";
    return "text-text-muted";
  }

  function statusLabel(status: string): string {
    return status.replaceAll("-", " ");
  }

  function orderNodes(nodes: readonly JobGraphNode[]): Array<JobGraphNode & { depth: number }> {
    const byParent = new Map<string | null, JobGraphNode[]>();
    for (const node of nodes) {
      const key = node.parentRunId ?? null;
      byParent.set(key, [...(byParent.get(key) ?? []), node]);
    }

    const result: Array<JobGraphNode & { depth: number }> = [];
    const visited = new Set<string>();
    const visit = (node: JobGraphNode, depth: number) => {
      if (visited.has(node.id)) return;
      visited.add(node.id);
      result.push({ ...node, depth });
      for (const child of byParent.get(node.id) ?? []) visit(child, depth + 1);
    };
    for (const root of byParent.get(null) ?? []) visit(root, 0);
    for (const node of nodes) visit(node, 0);
    return result;
  }
</script>

<article
  class={cn(
    "surface-card no-lift overflow-hidden border-l-2",
    graph.origin === JOB_GRAPH_ORIGIN.interactive ? "border-l-border-accent" : "border-l-border-default",
  )}
>
  <div class="flex items-start gap-3 p-3">
    <button
      type="button"
      class="mt-0.5 rounded-xs p-1 text-text-disabled transition-colors hover:bg-surface-3/60 hover:text-text-primary"
      onclick={() => void onToggle(graph)}
      aria-label={expanded ? "Collapse lane" : "Expand lane"}
    >
      {#if loadingDetail}
        <Loader2 class="h-4 w-4 animate-spin" />
      {:else if expanded}
        <ChevronDown class="h-4 w-4" />
      {:else}
        <ChevronRight class="h-4 w-4" />
      {/if}
    </button>

    <div class="min-w-0 flex-1">
      <div class="flex flex-wrap items-center gap-2">
        <StatusLed status={statusTone(graph.status)} pulse={graph.status === JOB_GRAPH_STATUS.running} />
        <h3 class="min-w-0 truncate text-sm font-semibold text-text-primary">{graph.displayName}</h3>
        <Badge variant={graph.origin === JOB_GRAPH_ORIGIN.interactive ? "accent" : "default"}>
          {graph.origin}
        </Badge>
        <span class="text-[0.65rem] uppercase tracking-[0.12em] text-text-disabled">
          {statusLabel(graph.status)}
        </span>
      </div>

      <div class="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-[0.7rem] text-text-muted">
        <span>{jobLabelForType(graph.currentNodeType, graph.status)}</span>
        <span>{terminalCount}/{nodeCount} nodes</span>
        {#if graph.rootEntityKind}
          <span>{graph.rootEntityKind}</span>
        {/if}
        <span>{formatRelativeTimeShort(graph.updatedAt)}</span>
        {#if warningCount > 0}
          <span class="flex items-center gap-1 text-status-warning-text">
            <AlertTriangle class="h-3 w-3" /> {warningCount} warning{warningCount === 1 ? "" : "s"}
          </span>
        {/if}
        {#if failedCount > 0}
          <span class="text-status-error-text">
            {failedCount} failed
          </span>
        {/if}
      </div>

      {#if graph.waitReason}
        <div class="mt-2 flex items-center gap-1.5 text-[0.72rem] text-status-warning-text">
          <CirclePause class="h-3.5 w-3.5" />
          {graph.waitReason}
        </div>
      {/if}

      <div class="mt-2 h-1 overflow-hidden rounded-xs bg-surface-3">
        <div
          class="h-full bg-text-accent transition-[width] duration-medium"
          style:width={`${progress}%`}
        ></div>
      </div>
    </div>

    {#if isCancellable}
      <Button
        type="button"
        variant="ghost"
        size="sm"
        class="shrink-0 gap-1.5 text-text-muted hover:text-status-error-text"
        disabled={cancelling}
        onclick={() => void onCancel(graph)}
      >
        {#if cancelling}<Loader2 class="h-3.5 w-3.5 animate-spin" />{:else}<Square class="h-3.5 w-3.5" />{/if}
        Cancel
      </Button>
    {/if}
  </div>

  {#if expanded}
    <div class="border-t border-border-subtle bg-surface-2/30 p-3">
      {#if loadingDetail}
        <div class="flex items-center justify-center gap-2 py-5 text-sm text-text-muted">
          <Loader2 class="h-4 w-4 animate-spin" /> Loading graph…
        </div>
      {:else if detail}
        {#if openSignals.length > 0}
          <div class="mb-3 space-y-1.5">
            {#each openSignals as signal (signal.id)}
              <div class="flex items-center gap-2 rounded-xs border border-status-warning/25 bg-status-warning/5 px-2.5 py-2 text-[0.72rem] text-status-warning-text">
                <CirclePause class="h-3.5 w-3.5" />
                <span>{signal.message ?? statusLabel(signal.kind)}</span>
              </div>
            {/each}
          </div>
        {/if}

        <div class="mb-2 flex items-center gap-2 text-[0.62rem] font-semibold uppercase tracking-[0.14em] text-text-disabled">
          <GitBranch class="h-3.5 w-3.5" /> Dependency graph
          <span>· {detail.dependencies.length} edges</span>
        </div>
        <div class="space-y-1">
          {#each orderedNodes as node (node.id)}
            <div
              class="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-3 rounded-xs border border-border-subtle/60 bg-surface-1/45 px-2.5 py-2"
              style:margin-left={`${Math.min(node.depth, 5) * 0.8}rem`}
            >
              <div class="min-w-0">
                <div class={cn("truncate text-[0.74rem] font-medium", nodeTone(node.status))}>
                  {jobLabelForType(node.type)}
                  {#if node.importance === JOB_NODE_IMPORTANCE.bestEffort}
                    <span class="ml-1 text-[0.6rem] font-normal uppercase tracking-[0.1em] text-text-disabled">best effort</span>
                  {/if}
                </div>
                <div class="mt-0.5 truncate text-[0.64rem] text-text-disabled">
                  {node.message ?? node.nodeKey ?? node.targetLabel ?? "Pending"}
                </div>
              </div>
              <div class="flex items-center gap-2 text-[0.62rem] text-text-disabled">
                {#if node.resourceClass !== JOB_RESOURCE_CLASS.light}
                  <Cpu class="h-3 w-3" />
                {/if}
                <span>{statusLabel(node.status)}</span>
              </div>
            </div>
          {/each}
        </div>
      {:else}
        <p class="py-4 text-center text-sm text-text-muted">Graph detail is unavailable.</p>
      {/if}
    </div>
  {/if}
</article>
