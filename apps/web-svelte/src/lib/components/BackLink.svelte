<script lang="ts">
  import { ArrowLeft, ChevronLeft } from "@lucide/svelte";
  import { page } from "$app/state";
  import { cn } from "@prismedia/ui-svelte";
  import { getBackHref } from "$lib/back-navigation";

  interface Props {
    fallback: string;
    label: string;
    variant?: "pill" | "text";
    class?: string;
  }

  let { fallback, label, variant = "pill", class: className }: Props = $props();

  const href = $derived(getBackHref(page.url.searchParams, fallback));
</script>

{#if variant === "text"}
  <a
    {href}
    class={cn(
      "inline-flex items-center gap-1 text-[0.78rem] text-text-muted hover:text-text-secondary transition-colors duration-fast",
      className,
    )}
  >
    <ChevronLeft class="h-3.5 w-3.5" />
    {label}
  </a>
{:else}
  <a
    {href}
    class={cn(
      "inline-flex items-center gap-1.5 surface-well px-2.5 py-1 text-[0.72rem] text-text-muted transition-colors duration-fast hover:text-text-accent",
      className,
    )}
  >
    <ArrowLeft class="h-3 w-3" />
    {label}
  </a>
{/if}
