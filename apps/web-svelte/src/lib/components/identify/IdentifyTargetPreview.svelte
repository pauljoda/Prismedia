<script lang="ts">
  import { Eye } from "@lucide/svelte";
  import ReviewSection from "$lib/components/review/ReviewSection.svelte";
  import IdentifyTargetPreviewBody from "./IdentifyTargetPreviewBody.svelte";
  import type { EntityCard } from "$lib/api/entities";

  interface Props {
    /** The library entity currently being identified. */
    entity: EntityCard;
  }

  let { entity }: Props = $props();
</script>

<ReviewSection panelId="identify-preview" title="To Identify" startCollapsed>
  {#snippet icon()}
    <Eye class="h-3.5 w-3.5 text-text-accent" />
  {/snippet}
  {#snippet children()}
    <!-- Shown for every entity kind so the user can confirm what is being matched.
         Mounted lazily by the collapsed section, so thumbnails are only fetched once opened. -->
    <IdentifyTargetPreviewBody {entity} />
  {/snippet}
</ReviewSection>
