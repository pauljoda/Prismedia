<script lang="ts">
  import type { Snippet } from "svelte";
  import BackLink from "$lib/components/BackLink.svelte";

  interface Props {
    /** Concept name shown in the back link, e.g. "Control room". */
    concept: string;
    children: Snippet;
  }

  let { concept, children }: Props = $props();
</script>

<!-- Shared page frame for the management concepts: a quiet way back to the gallery, then the page. -->
<div class="mx-auto flex w-full max-w-6xl min-w-0 flex-col gap-6 pb-10 md:px-3">
  <BackLink fallback="/concepts" label="Concept · {concept}" variant="text" class="self-start" />
  {@render children()}
</div>
