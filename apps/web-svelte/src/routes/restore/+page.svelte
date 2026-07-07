<script lang="ts">
  import { goto } from "$app/navigation";
  import { onDestroy, onMount } from "svelte";
  import { AlertTriangle, CheckCircle2, Database, Loader2, RotateCcw } from "@lucide/svelte";
  import { Button, Panel } from "@prismedia/ui-svelte";
  import { fetchDatabaseRestoreStatus } from "$lib/api/settings";

  type RestorePhase = "connecting" | "restoring" | "complete" | "failed";

  const PollIntervalMs = 1_500;
  const RedirectDelayMs = 1_000;

  let phase = $state<RestorePhase>("connecting");
  let detail = $state("Waiting for Prismedia to begin restoring the database.");
  let error = $state<string | null>(null);
  let pollTimer: number | null = null;
  let redirectTimer: number | null = null;

  const title = $derived.by(() => {
    if (phase === "complete") return "Restore Complete";
    if (phase === "failed") return "Restore Failed";
    return "Restoring Database";
  });

  onMount(() => {
    void pollRestoreStatus();
  });

  onDestroy(() => {
    clearTimers();
  });

  async function pollRestoreStatus() {
    try {
      const status = await fetchDatabaseRestoreStatus();
      if (status.restoreFailed) {
        phase = "failed";
        error = status.error;
        detail = "Prismedia could not complete the restore. Review the error and choose another backup if needed.";
        return;
      }

      if (status.restorePending) {
        phase = "restoring";
        detail = "The selected backup is being applied. Prismedia may be unavailable for a moment.";
        schedulePoll();
        return;
      }

      phase = "complete";
      detail = "Prismedia is ready again. Returning to the dashboard.";
      redirectTimer = window.setTimeout(() => {
        void goto("/", { replaceState: true });
      }, RedirectDelayMs);
    } catch {
      phase = "connecting";
      detail = "Prismedia is restarting. This page will reconnect automatically.";
      schedulePoll();
    }
  }

  function schedulePoll() {
    if (pollTimer !== null) {
      window.clearTimeout(pollTimer);
    }

    pollTimer = window.setTimeout(() => {
      pollTimer = null;
      void pollRestoreStatus();
    }, PollIntervalMs);
  }

  function clearTimers() {
    if (pollTimer !== null) {
      window.clearTimeout(pollTimer);
      pollTimer = null;
    }

    if (redirectTimer !== null) {
      window.clearTimeout(redirectTimer);
      redirectTimer = null;
    }
  }
</script>

<div class="mx-auto flex min-h-[calc(100dvh-8rem)] w-full max-w-2xl items-center">
  <Panel class="w-full">
    <div class="space-y-5 p-6 text-center">
      <div class="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl border border-border-subtle bg-surface-panel text-text-accent shadow-glow-soft">
        {#if phase === "complete"}
          <CheckCircle2 class="h-7 w-7 text-status-success-text" />
        {:else if phase === "failed"}
          <AlertTriangle class="h-7 w-7 text-status-error-text" />
        {:else}
          <Database class="h-7 w-7" />
        {/if}
      </div>

      <div class="space-y-2">
        <h1 class="text-lg font-semibold text-text-primary">{title}</h1>
        <p class="mx-auto max-w-lg text-sm leading-relaxed text-text-muted">
          {detail}
        </p>
      </div>

      {#if phase === "failed" && error}
        <div class="surface-panel border-l-2 border-status-error px-3 py-2 text-left text-[0.78rem] text-status-error-text">
          {error}
        </div>
      {/if}

      {#if phase === "complete"}
        <div class="flex items-center justify-center gap-2 text-[0.75rem] text-status-success-text">
          <CheckCircle2 class="h-4 w-4" />
          Ready
        </div>
      {:else if phase === "failed"}
        <div class="flex flex-wrap items-center justify-center gap-2">
          <Button type="button" variant="secondary" class="gap-2" onclick={() => void goto("/settings/database-backups")}>
            <RotateCcw class="h-4 w-4" />
            Back to Settings
          </Button>
          <Button type="button" variant="ghost" onclick={() => void pollRestoreStatus()}>
            Check Again
          </Button>
        </div>
      {:else}
        <div class="flex items-center justify-center gap-2 text-[0.75rem] text-text-muted">
          <Loader2 class="h-4 w-4 animate-spin" />
          Please wait
        </div>
      {/if}
    </div>
  </Panel>
</div>
