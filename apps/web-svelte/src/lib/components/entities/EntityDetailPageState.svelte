<script module lang="ts">
  export type EntityDetailPageLoadState = "loading" | "ready" | "error";
</script>

<script lang="ts">
  import { Button } from "@prismedia/ui-svelte";
  import type { Snippet } from "svelte";
  import EntityDetailSkeleton from "./EntityDetailSkeleton.svelte";
  import type { EntityDetailPosterSize } from "./EntityDetail.svelte";

  interface Props {
    children?: Snippet;
    errorMessage?: string | null;
    fallbackError: string;
    loadState: EntityDetailPageLoadState;
    onRetry: () => void;
    posterAspect?: string;
    posterSize?: EntityDetailPosterSize;
    showHero?: boolean;
    tabCount?: number;
  }

  let {
    children,
    errorMessage = null,
    fallbackError,
    loadState,
    onRetry,
    posterAspect = "2 / 3",
    posterSize = "large",
    showHero = true,
    tabCount = 3,
  }: Props = $props();
</script>

{#if loadState === "loading"}
  <EntityDetailSkeleton {posterAspect} {posterSize} {showHero} {tabCount} />
{:else if loadState === "error"}
  <div class="error-notice" role="alert">
    <p>{errorMessage ?? fallbackError}</p>
    <Button variant="secondary" size="sm" onclick={onRetry}>Retry</Button>
  </div>
{:else}
  {#if children}{@render children()}{/if}
{/if}

<style>
  .error-notice {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    padding: 1rem;
    border: 1px solid color-mix(in srgb, var(--color-error, #ef4444) 50%, var(--color-border, #1c2235));
    border-radius: var(--radius-xs, 4px);
    background: var(--color-surface-2, #101420);
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.85rem;
  }

  .error-notice p {
    margin: 0;
  }
</style>
