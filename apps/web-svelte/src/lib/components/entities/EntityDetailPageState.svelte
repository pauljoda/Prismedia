<script module lang="ts">
  export type EntityDetailPageLoadState = "loading" | "ready" | "error";
</script>

<script lang="ts">
  import { Button, Alert } from "@prismedia/ui-svelte";
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
  <Alert.Root variant="destructive" class="flex items-center justify-between gap-4 p-4">
    <Alert.Description>{errorMessage ?? fallbackError}</Alert.Description>
    <Button variant="outline" size="sm" onclick={onRetry}>Retry</Button>
  </Alert.Root>
{:else}
  {#if children}{@render children()}{/if}
{/if}
