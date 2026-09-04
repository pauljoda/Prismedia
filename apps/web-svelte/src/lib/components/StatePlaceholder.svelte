<script lang="ts">
  import type { Component, Snippet } from "svelte";
  import { Empty } from "@prismedia/ui-svelte";
  import PrismediaLoadingMark from "./PrismediaLoadingMark.svelte";

  interface Props {
    /** Contextual icon (lucide component) shown centered in the badge. */
    icon: Component;
    title: string;
    description?: string;
    /** Renders a spinning accent ring around the icon to signal active work. */
    busy?: boolean;
    /** Optional action row rendered beneath the explanatory copy. */
    children?: Snippet;
  }

  let { icon, title, description, busy = false, children }: Props = $props();
  const Icon = $derived(icon);
</script>

<Empty.Root
  class="min-h-36 rounded-md border border-dashed border-border bg-card/50 p-6"
  role={busy ? undefined : "status"}
  aria-busy={busy || undefined}
>
  <Empty.Header>
    {#if busy}
      <PrismediaLoadingMark label={title} compact />
    {:else}
      <Empty.Media variant="icon">
        <Icon aria-hidden="true" />
      </Empty.Media>
    {/if}
    <Empty.Title class="text-base font-semibold">{title}</Empty.Title>
    {#if description}
      <Empty.Description>{description}</Empty.Description>
    {/if}
  </Empty.Header>
  {#if children}
    <Empty.Content>{@render children()}</Empty.Content>
  {/if}
</Empty.Root>
