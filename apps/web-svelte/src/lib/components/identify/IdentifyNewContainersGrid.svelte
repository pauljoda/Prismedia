<script lang="ts">
  import { FolderPlus } from "@lucide/svelte";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";
  import {
    reviewImagePreviewUrl,
    structuralDescendantProposals,
  } from "$lib/components/identify-review";
  import { aspectRatioForKind, toAspectRatioValue } from "$lib/entities/entity-thumbnail";

  interface Props {
    /** Provider containers that applying will create (volumes, seasons, discs, …). */
    containers: EntityMetadataProposal[];
    /** Called when a container is activated, to drill into the children filed inside it. */
    onWalkChild?: (container: EntityMetadataProposal) => void;
  }

  let { containers, onWalkChild }: Props = $props();

  function containerCover(container: EntityMetadataProposal): string | undefined {
    const image = container.images?.find((img) => img.kind === "cover" || img.kind === "poster" || img.kind === "thumbnail");
    return image ? reviewImagePreviewUrl(image, container.targetKind) : undefined;
  }

  function matchedInside(container: EntityMetadataProposal): number {
    return structuralDescendantProposals(container).filter((node) => Boolean(node.targetEntityId)).length;
  }
</script>

<div class="containers-grid p-3.5">
  {#each containers as container (container.proposalId)}
    {@const cover = containerCover(container)}
    {@const title = container.patch?.title ?? container.targetKind}
    {@const filed = matchedInside(container)}
    <div class="container-tile">
      <div class="container-cover-wrap">
        <button
          type="button"
          class="container-cover"
          style="aspect-ratio: {toAspectRatioValue(aspectRatioForKind(container.targetKind))};"
          onclick={() => onWalkChild?.(container)}
          aria-label={`Review new ${title}`}
        >
          {#if cover}
            <img src={cover} alt={title} loading="lazy" referrerpolicy="no-referrer" />
          {:else}
            <div class="container-cover-empty"></div>
          {/if}
          <span class="container-new-badge"><FolderPlus class="h-3 w-3" /><span>New</span></span>
        </button>
      </div>
      <div class="container-title" title={title}>{title}</div>
      <div class="container-subtitle">{filed} filed inside</div>
    </div>
  {/each}
</div>

<style>
  .containers-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(min(8rem, 100%), 9.5rem));
    justify-content: start;
    gap: 0.5rem;
  }

  .container-tile { display: grid; gap: 0.3rem; }
  .container-cover-wrap { position: relative; }
  .container-cover { position: relative; display: block; width: 100%; overflow: hidden; border-radius: var(--radius-sm, 6px); border: 1px solid var(--color-border-accent, #6b5526); background: var(--color-surface-2, #101420); cursor: pointer; }
  .container-cover img { width: 100%; height: 100%; object-fit: cover; }
  .container-cover-empty { width: 100%; height: 100%; background: linear-gradient(135deg, #141925, #0d1119); }
  .container-new-badge { position: absolute; left: 0.3rem; bottom: 0.3rem; display: inline-flex; align-items: center; gap: 0.25rem; border-radius: 4px; border: 1px solid var(--color-border-accent-strong, #d59a2a); background: color-mix(in srgb, #060810 65%, transparent); color: var(--color-text-accent, #f2c26a); font-size: 0.58rem; font-family: var(--font-mono, monospace); padding: 0.12rem 0.35rem; }
  .container-title { font-size: 0.72rem; line-height: 1.2; color: var(--color-text-primary, #f2eed8); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .container-subtitle { font-size: 0.62rem; line-height: 1.2; color: var(--color-text-muted, #8a93a6); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
