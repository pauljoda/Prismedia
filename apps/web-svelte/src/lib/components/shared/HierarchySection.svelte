<script lang="ts">
  import type { Snippet } from "svelte";

  interface Props {
    title: string;
    children: Snippet;
    action?: Snippet;
  }

  let { title, children, action }: Props = $props();
</script>

<section class="space-y-2.5">
  <div class="flex items-center justify-between gap-3">
    <h2 class="text-[0.78rem] font-semibold uppercase tracking-[0.14em] text-text-muted">
      {title}
    </h2>
    {#if action}{@render action()}{/if}
  </div>
  {@render children()}
</section>
