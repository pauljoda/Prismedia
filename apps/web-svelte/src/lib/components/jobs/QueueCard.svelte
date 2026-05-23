<script lang="ts">
  import { Play, Square } from "@lucide/svelte";
  import { StatusLed, cn } from "@prismedia/ui-svelte";
  import type { QueueSummary } from "$lib/jobs/models";
  import { getQueueIcon, ledForQueue } from "$lib/jobs/helpers";

  interface Props {
    queue: QueueSummary;
    runningQueue: string | null;
    cancellingQueue: string | null;
    acknowledging: "all" | string | null;
    onRun: (queueName: string) => void | Promise<void>;
    onCancel: (queueName: string) => void | Promise<void>;
    onClearFailures: (scope: "all" | string) => void | Promise<void>;
  }

  let {
    queue,
    runningQueue,
    cancellingQueue,
    acknowledging,
    onRun,
    onCancel,
    onClearFailures,
  }: Props = $props();

  const Icon = $derived(getQueueIcon(queue.name));
  const hasPressure = $derived(queue.active > 0 || queue.backlog > 0);
</script>

{#snippet metric(label: string, value: number, highlight: boolean, danger: boolean = false)}
  <div class="bg-black/15 px-2 py-2 text-center">
    <p class="mb-0.5 text-[0.55rem] uppercase tracking-wider text-text-disabled">
      {label}
    </p>
    <p
      class={cn(
        "text-mono text-[0.84rem] font-semibold",
        danger && highlight
          ? "text-status-error-text"
          : highlight
            ? "text-text-accent"
            : "text-text-muted",
      )}
    >
      {value}
    </p>
  </div>
{/snippet}

<div
  class={cn(
    "surface-card no-lift space-y-4 p-4 transition-all duration-normal",
    queue.failed > 0
      ? "border-status-error/25"
      : hasPressure
        ? "border-border-accent/30"
        : "",
  )}
>
  <div class="flex flex-wrap items-start justify-between gap-3">
    <div class="min-w-0">
      <div class="flex items-center gap-2.5">
        <StatusLed status={ledForQueue(queue.status)} pulse={queue.active > 0} />
        <Icon class="h-4 w-4 text-text-muted" />
        <h3 class="truncate text-[0.92rem] font-heading font-semibold">{queue.label}</h3>
      </div>
      <p class="mt-1 text-[0.72rem] text-text-muted">{queue.description}</p>
      <p class="mt-1 text-[0.68rem] text-text-disabled">
        Throttle: {queue.concurrency} worker{queue.concurrency === 1 ? "" : "s"} at a time
      </p>
    </div>
    <div class="flex flex-wrap items-center gap-1.5">
      {#if queue.failed > 0}
        <button
          type="button"
          onclick={() => void onClearFailures(queue.name)}
          disabled={acknowledging !== null}
          class="px-2 py-1 text-xs text-text-muted transition-colors hover:text-status-error-text disabled:opacity-40"
        >
          {acknowledging === queue.name ? "Clearing..." : "Clear failures"}
        </button>
      {/if}
      {#if queue.active > 0 || queue.backlog > 0}
        <button
          type="button"
          onclick={() => void onCancel(queue.name)}
          disabled={cancellingQueue === queue.name}
          class="flex items-center gap-1 px-2 py-1 text-xs text-text-muted transition-colors hover:text-status-error-text disabled:opacity-40"
        >
          <Square class="h-3 w-3" />
          {cancellingQueue === queue.name ? "Stopping..." : "Stop"}
        </button>
      {/if}
      <button
        type="button"
        onclick={() => void onRun(queue.name)}
        disabled={runningQueue === queue.name}
        class="flex items-center gap-1 px-2 py-1 text-xs text-text-muted transition-colors hover:text-text-accent disabled:opacity-40"
      >
        <Play class="h-3 w-3" />
        {runningQueue === queue.name ? "Queueing..." : "Run"}
      </button>
    </div>
  </div>

  <div class="grid grid-cols-4 gap-2">
    {@render metric("Running", queue.active, queue.active > 0)}
    {@render metric("Queued", queue.waiting, queue.waiting > 0)}
    {@render metric("Delayed", queue.delayed, queue.delayed > 0)}
    {@render metric("Errors", queue.failed, queue.failed > 0, true)}
  </div>
</div>
