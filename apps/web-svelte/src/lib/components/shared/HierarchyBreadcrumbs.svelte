<script lang="ts">
  import { ChevronRight } from "@lucide/svelte";

  interface BreadcrumbItem {
    id: string;
    title: string;
    href: string;
  }

  interface Props {
    items: BreadcrumbItem[];
  }

  let { items }: Props = $props();
</script>

{#if items.length > 0}
  <nav class="flex flex-wrap items-center gap-1 text-[0.72rem] text-text-muted">
    {#each items as item, index (item.id)}
      <div class="flex items-center gap-1">
        {#if index > 0}
          <ChevronRight class="h-3 w-3 text-text-disabled" />
        {/if}
        <a
          href={item.href}
          class="transition-colors duration-fast hover:text-text-accent"
        >
          {item.title}
        </a>
      </div>
    {/each}
  </nav>
{/if}
