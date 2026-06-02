<script lang="ts">
  import { Check, Clock, Loader2 } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { assetUrl } from "$lib/api/orval-fetch";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";
  import { reviewImagePreviewUrl, type StructuralChildEntity } from "$lib/components/identify-review";
  import { aspectRatioForKind, toAspectRatioValue } from "$lib/entities/entity-thumbnail";
  import { useIdentifyStore } from "./identify-store.svelte";

  interface Props {
    childEntities: StructuralChildEntity[];
    proposal: EntityMetadataProposal;
    /** True while the background cascade is still streaming children into this proposal. */
    cascadeRunning?: boolean;
    /** Called when a matched child is activated, to drill into its own review. */
    onWalkChild?: (child: EntityMetadataProposal) => void;
  }

  let { childEntities, proposal, cascadeRunning = false, onWalkChild }: Props = $props();

  const store = useIdentifyStore();

  const matchedIds = $derived(
    new Set((proposal.children ?? []).map((child) => child.targetEntityId).filter((id): id is string => Boolean(id))),
  );

  // The cascade resolves children serially in this order, flushing each as it matches. So everything
  // up to the last matched child has been processed (matched, or attempted with no match), the next
  // one is what's currently being identified, and the rest are still queued.
  const lastMatchedIndex = $derived.by(() => {
    let last = -1;
    childEntities.forEach((child, index) => {
      if (matchedIds.has(child.id)) last = index;
    });
    return last;
  });

  function matchedProposal(childId: string): EntityMetadataProposal | null {
    return (proposal.children ?? []).find((child) => child.targetEntityId === childId) ?? null;
  }

  function statusFor(childId: string, index: number): "matched" | "loading" | "queued" | "none" {
    if (matchedIds.has(childId)) return "matched";
    if (!cascadeRunning) return "none";
    if (index <= lastMatchedIndex) return "none"; // already processed, no match found
    if (index === lastMatchedIndex + 1) return "loading"; // currently identifying
    return "queued";
  }

  function coverFor(childId: string, fallback: string | null): string | undefined {
    const matched = matchedProposal(childId);
    // The matched proposal's image is an external provider URL (e.g. Cover Art Archive); resolve it
    // through the review helper rather than assetUrl, which only handles local relative paths.
    const image = matched?.images?.find((img) => img.kind === "cover" || img.kind === "poster" || img.kind === "thumbnail");
    if (image) return reviewImagePreviewUrl(image, matched?.targetKind);
    return assetUrl(fallback ?? undefined) || undefined;
  }
</script>

<div class="grid grid-cols-2 gap-2 p-3.5 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
  {#each childEntities as child, index (child.id)}
    {@const matched = matchedProposal(child.id)}
    {@const status = statusFor(child.id, index)}
    {@const cover = coverFor(child.id, child.coverUrl)}
    {@const noMatch = status === "none"}
    {@const selected = matched ? store.isReviewProposalSelected(matched.proposalId) : false}
    <div class={cn("child-tile", selected && "is-selected")}>
      <div class="child-cover-wrap">
        <button
          type="button"
          class={cn("child-cover", noMatch && "is-nomatch")}
          style="aspect-ratio: {toAspectRatioValue(aspectRatioForKind(child.kind))};"
          onclick={() => matched && onWalkChild?.(matched)}
          aria-label={matched ? `Review ${matched.patch?.title ?? child.title}` : child.title}
        >
          {#if cover}
            <img src={cover} alt={child.title} loading="lazy" />
          {:else}
            <div class="child-cover-empty"></div>
          {/if}

          {#if status === "loading"}
            <div class="child-overlay"><Loader2 class="h-5 w-5 animate-spin" /><span>Identifying…</span></div>
          {:else if status === "queued"}
            <div class="child-overlay child-overlay-muted"><Clock class="h-4 w-4" /><span>Queued</span></div>
          {:else if noMatch}
            <div class="child-overlay child-overlay-muted"><span>No match found</span></div>
          {/if}
        </button>

        {#if matched}
          <button
            type="button"
            class={cn("child-select", selected && "is-on")}
            onclick={() => matched && store.setReviewProposalSelected(matched.proposalId, !selected)}
            aria-label={selected ? "Deselect" : "Select"}
          >
            {#if selected}<Check class="h-3.5 w-3.5" />{/if}
          </button>
        {/if}
      </div>

      <div class={cn("child-title", noMatch && "is-muted")} title={matched?.patch?.title ?? child.title}>
        {matched?.patch?.title ?? child.title}
      </div>
    </div>
  {/each}
</div>

<style>
  .child-tile { display: grid; gap: 0.3rem; }
  .child-cover-wrap { position: relative; }
  .child-cover { position: relative; display: block; width: 100%; overflow: hidden; border-radius: var(--radius-sm, 6px); border: 1px solid var(--color-border-subtle, #1c2235); background: var(--color-surface-2, #101420); cursor: pointer; }
  .child-cover.is-nomatch { cursor: default; }
  .child-cover.is-nomatch img { filter: grayscale(1); opacity: 0.4; }
  .child-cover img { width: 100%; height: 100%; object-fit: cover; }
  .child-cover-empty { width: 100%; height: 100%; background: linear-gradient(135deg, #141925, #0d1119); }
  .is-selected .child-cover { border-color: var(--color-border-accent-strong, #d59a2a); box-shadow: 0 0 0 1px var(--color-border-accent-strong, #d59a2a); }
  .child-overlay { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 0.3rem; background: color-mix(in srgb, #060810 60%, transparent); color: var(--color-text-accent, #f2c26a); font-size: 0.66rem; text-align: center; padding: 0 0.4rem; }
  .child-overlay-muted { color: var(--color-text-muted, #8a93a6); }
  .child-select { position: absolute; right: 0.3rem; top: 0.3rem; width: 1.15rem; height: 1.15rem; display: flex; align-items: center; justify-content: center; border-radius: 4px; border: 1px solid var(--color-border, #1c2235); background: color-mix(in srgb, #060810 55%, transparent); color: var(--color-text-accent, #f2c26a); cursor: pointer; }
  .child-select.is-on { background: var(--color-border-accent-strong, #d59a2a); border-color: var(--color-border-accent-strong, #d59a2a); color: #0c0f15; }
  .child-title { font-size: 0.72rem; line-height: 1.2; color: var(--color-text-primary, #f2eed8); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .child-title.is-muted { color: var(--color-text-muted, #8a93a6); }
</style>
