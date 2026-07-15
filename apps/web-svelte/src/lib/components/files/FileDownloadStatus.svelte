<script lang="ts">
  import { Archive } from "@lucide/svelte";
  import { Meter } from "@prismedia/ui-svelte";
  import PrismediaLoadingMark from "$lib/components/PrismediaLoadingMark.svelte";

  interface Props {
    fileName: string;
    message: string;
    detail: string;
    progressPercent: number | null;
  }

  let { fileName, message, detail, progressPercent }: Props = $props();
</script>

<aside class="download-status floating-surface" aria-live="polite" aria-atomic="true">
  <div class="status-heading">
    <span class="status-icon"><Archive class="h-4 w-4" /></span>
    <div>
      <p class="status-kicker">Folder download</p>
      <h2>{fileName}</h2>
    </div>
  </div>

  <p class="status-message">{message}</p>
  {#if progressPercent === null}
    <PrismediaLoadingMark
      compact
      label={message}
      height={34}
      markSize={22}
      class="preparation-mark"
    />
  {:else}
    <Meter value={progressPercent} showValue label="Archive progress" />
  {/if}
  <p class="status-detail">{detail}</p>
</aside>

<style>
  .download-status {
    position: fixed;
    z-index: 80;
    right: max(1rem, env(safe-area-inset-right));
    bottom: max(1rem, env(safe-area-inset-bottom));
    display: grid;
    width: min(22rem, calc(100vw - 2rem));
    gap: 0.7rem;
    padding: 0.9rem 1rem;
    border-left: 2px solid var(--color-border-accent);
  }

  .status-heading {
    display: flex;
    align-items: center;
    gap: 0.65rem;
    min-width: 0;
  }

  .status-icon {
    display: grid;
    width: 2rem;
    height: 2rem;
    flex: 0 0 auto;
    place-items: center;
    border: 1px solid var(--color-border-default);
    border-radius: var(--radius-sm);
    color: var(--color-text-secondary);
    background: var(--color-surface-3);
  }

  .status-kicker,
  .status-message,
  .status-detail,
  h2 {
    margin: 0;
  }

  .status-kicker {
    color: var(--color-text-muted);
    font: 600 0.65rem/1.2 var(--font-inter, Inter), sans-serif;
    letter-spacing: 0.08em;
    text-transform: uppercase;
  }

  h2 {
    overflow: hidden;
    color: var(--color-text-primary);
    font: 600 0.9rem/1.35 var(--font-geist, Geist), sans-serif;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .status-message {
    color: var(--color-text-secondary);
    font: 500 0.8rem/1.4 var(--font-inter, Inter), sans-serif;
  }

  .status-detail {
    color: var(--color-text-muted);
    font: 500 0.7rem/1.35 var(--font-jetbrains-mono, "JetBrains Mono"), monospace;
  }

  .download-status :global(.preparation-mark) {
    width: 100%;
    min-width: 0;
  }
</style>
