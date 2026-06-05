<script lang="ts">
  import { Eye } from "@lucide/svelte";
  import IdentifyReviewSection from "./IdentifyReviewSection.svelte";
  import IdentifyTargetPreviewBody from "./IdentifyTargetPreviewBody.svelte";
  import type { EntityCard } from "$lib/api/entities";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";

  interface Props {
    /** The library entity currently being identified. */
    entity: EntityCard;
  }

  let { entity }: Props = $props();

  // Visual kinds get a "To Identify" preview so the user can confirm what they
  // are matching. People, studios, and tags are non-visual and already show a
  // thumbnail elsewhere, so the section is omitted for them entirely.
  const VISUAL_KINDS = new Set<string>([
    ENTITY_KIND.book,
    ENTITY_KIND.video,
    ENTITY_KIND.movie,
    ENTITY_KIND.image,
    ENTITY_KIND.gallery,
    ENTITY_KIND.videoSeries,
    ENTITY_KIND.videoSeason,
  ]);
  const isVisualKind = $derived(VISUAL_KINDS.has(entity.kind));
</script>

{#if isVisualKind}
  <IdentifyReviewSection panelId="identify-preview" title="To Identify" startCollapsed>
    {#snippet icon()}
      <Eye class="h-3.5 w-3.5 text-text-accent" />
    {/snippet}
    {#snippet children()}
      <!-- Mounted lazily by the section on first expand, so thumbnails are only
           fetched once the user opens the preview. -->
      <IdentifyTargetPreviewBody {entity} />
    {/snippet}
  </IdentifyReviewSection>
{/if}
