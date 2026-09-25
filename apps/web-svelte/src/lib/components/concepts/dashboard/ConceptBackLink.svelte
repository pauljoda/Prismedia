<script lang="ts">
  import { resolve } from "$app/paths";
  import { ArrowLeft } from "@lucide/svelte";

  interface Props {
    /** Concept title as listed in the concept gallery. */
    title: string;
  }

  let { title }: Props = $props();
</script>

<!-- Small return path to the concept gallery so the product owner can compare concepts side by side. -->
<a
  href={resolve("/concepts")}
  class="inline-flex min-h-8 w-fit items-center gap-2 rounded-[var(--radius-xs)] font-mono text-[0.68rem] uppercase tracking-[0.16em] text-text-muted transition-colors hover:text-text-primary focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--color-accent)]"
>
  <ArrowLeft class="h-3.5 w-3.5" aria-hidden="true" />
  <span>Concept</span>
  <span aria-hidden="true">·</span>
  <span class="text-text-secondary">{title}</span>
</a>
