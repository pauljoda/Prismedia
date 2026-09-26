<script lang="ts">
  import { Puzzle } from "@lucide/svelte";

  interface Props {
    name: string;
    iconUrl?: string | null;
    class?: string;
  }

  let { name, iconUrl = null, class: className = "size-10" }: Props = $props();
  let failedUrl = $state<string | null>(null);
  const visibleUrl = $derived(iconUrl && failedUrl !== iconUrl ? iconUrl : null);
</script>

<span
  class={`grid shrink-0 place-items-center overflow-hidden rounded-md bg-surface-raised ring-1 ring-inset ring-border-subtle/70 ${className}`}
  aria-hidden="true"
  title={name}
>
  {#if visibleUrl}
    <img
      src={visibleUrl}
      alt=""
      class="size-full object-contain p-[12%]"
      loading="lazy"
      decoding="async"
      onerror={() => (failedUrl = visibleUrl)}
    />
  {:else}
    <Puzzle class="size-[48%] text-text-disabled" strokeWidth={1.5} />
  {/if}
</span>
