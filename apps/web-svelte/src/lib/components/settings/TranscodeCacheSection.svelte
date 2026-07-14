<script lang="ts">
  import { onMount } from "svelte";
  import { HardDrive, Loader2, Trash2 } from "@lucide/svelte";
  import { Button, Panel, cn } from "@prismedia/ui-svelte";
  import {
    clearTranscodeCache,
    fetchTranscodeCacheStatus,
    type SettingsCatalogResponse,
    type SettingValue,
    type TranscodeCacheStatus,
  } from "$lib/api/settings";
  import { findSetting } from "$lib/settings/app-settings";
  import SettingsControl from "$lib/components/settings/SettingsControl.svelte";
  import StorageStat from "$lib/components/settings/StorageStat.svelte";
  import ConfirmDialog from "$lib/components/entities/ConfirmDialog.svelte";

  const MAX_CACHE_SETTING_KEY = "hls.maxCacheSizeGb";
  const BYTES_PER_GB = 1024 * 1024 * 1024;

  interface Props {
    catalog: SettingsCatalogResponse | null;
    onCommit: (key: string, value: SettingValue) => void;
  }

  let { catalog, onCommit }: Props = $props();

  let status = $state<TranscodeCacheStatus | null>(null);
  let loading = $state(true);
  let clearing = $state(false);
  let localError = $state<string | null>(null);
  let localMessage = $state<string | null>(null);
  let clearDialogOpen = $state(false);

  const maxSetting = $derived(findSetting(catalog, MAX_CACHE_SETTING_KEY));
  const usagePercent = $derived(
    status && status.maxBytes > 0
      ? Math.min(100, Math.round((status.usedBytes / status.maxBytes) * 100))
      : null,
  );
  const overLimit = $derived(usagePercent !== null && usagePercent >= 100);

  onMount(() => {
    void refresh();
  });

  async function refresh() {
    loading = true;
    try {
      status = await fetchTranscodeCacheStatus();
      localError = null;
    } catch (err) {
      localError = err instanceof Error ? err.message : "Failed to read cache size";
    } finally {
      loading = false;
    }
  }

  function handleClear() {
    clearDialogOpen = true;
  }

  async function confirmClear() {
    clearing = true;
    localError = null;
    try {
      status = await clearTranscodeCache();
      flash("Transcode cache cleared.");
    } catch (err) {
      localError = err instanceof Error ? err.message : "Failed to clear cache";
    } finally {
      clearing = false;
    }
  }

  // Saving the limit changes the configured maximum; reflect it immediately, then persist via the page.
  function handleLimitCommit(key: string, value: SettingValue) {
    if (typeof value === "number" && status) {
      status = { ...status, maxBytes: value > 0 ? value * BYTES_PER_GB : 0 };
    }
    onCommit(key, value);
  }

  function flash(m: string, ms = 2500) {
    localMessage = m;
    setTimeout(() => {
      if (localMessage === m) localMessage = null;
    }, ms);
  }

  function formatBytes(bytes: number): string {
    if (!Number.isFinite(bytes) || bytes <= 0) return "0 B";
    const units = ["B", "KB", "MB", "GB", "TB"];
    let value = bytes;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit += 1;
    }
    const decimals = unit > 0 && value < 100 ? 1 : 0;
    return `${value.toFixed(decimals)} ${units[unit]}`;
  }
</script>

<Panel>
  <div class="p-5 space-y-5">
    <div class="flex items-center gap-2.5">
      <HardDrive class="h-4 w-4 text-text-accent" />
      <div>
        <h2 class="text-kicker text-text-primary">Transcode Cache</h2>
        <p class="text-[0.68rem] text-text-muted">
          Prepared video segments are kept on disk so repeat plays and seeks are instant. They are
          regenerated on demand, so clearing or capping them is always safe.
        </p>
      </div>
    </div>

    {#if localError}
      <div class="surface-panel border-l-2 border-status-error px-3 py-2 text-[0.78rem] text-status-error-text">
        {localError}
      </div>
    {:else if localMessage}
      <div class="surface-panel border-l-2 border-status-success px-3 py-2 text-[0.78rem] text-status-success-text">
        {localMessage}
      </div>
    {/if}

    <div class="grid gap-3 sm:grid-cols-2">
      <StorageStat
        label="On disk"
        value={loading && !status ? "…" : formatBytes(status?.usedBytes ?? 0)}
        accent={overLimit}
        gradientClass={overLimit ? "bg-status-error" : undefined}
      />
      <StorageStat
        label="Limit"
        value={status && status.maxBytes > 0 ? formatBytes(status.maxBytes) : "No limit"}
      />
    </div>

    <!-- Usage bar (only meaningful when a limit is set) -->
    {#if usagePercent !== null}
      <div class="space-y-1.5">
        <div class="h-2 w-full overflow-hidden rounded-full bg-surface-1 shadow-[inset_0_1px_3px_rgba(0,0,0,0.25)]">
          <div
            class={cn(
              "h-full rounded-full transition-all duration-fast",
              overLimit ? "bg-status-error" : "bg-accent-500",
            )}
            style:width={`${usagePercent}%`}
          ></div>
        </div>
        <p class="text-[0.66rem] text-text-muted">
          {usagePercent}% of the limit used.{overLimit ? " Oldest cached videos are removed automatically." : ""}
        </p>
      </div>
    {/if}

    <!-- Limit control + clear action -->
    <div class="grid gap-4 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
      <div class="surface-well px-4">
        {#if maxSetting}
          <SettingsControl setting={maxSetting} onCommit={handleLimitCommit} />
        {/if}
      </div>
      <Button
        type="button"
        variant="secondary"
        disabled={clearing || (status?.usedBytes ?? 0) === 0}
        onclick={() => void handleClear()}
        class="gap-2 text-status-error-text hover:border-border-accent/25"
      >
        {#if clearing}
          <Loader2 class="h-4 w-4 animate-spin" />
        {:else}
          <Trash2 class="h-4 w-4" />
        {/if}
        Clear cache now
      </Button>
    </div>
  </div>
</Panel>

<ConfirmDialog
  open={clearDialogOpen}
  title="Clear transcode cache?"
  message="Videos will re-prepare the next time they are played. Your media and playback progress are not affected."
  confirmLabel="Clear cache"
  danger
  onConfirm={confirmClear}
  onClose={() => (clearDialogOpen = false)}
/>
