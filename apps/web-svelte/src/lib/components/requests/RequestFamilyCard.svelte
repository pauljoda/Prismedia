<script module lang="ts">
  import type { Component } from "svelte";

  /** A source that can find one request kind: an installed provider or a connected catalog. */
  export interface RequestKindSource {
    id: string;
    name: string;
    iconUrl?: string | null;
  }

  /** One requestable kind inside a media family. */
  export interface RequestFamilyKind {
    kind: string;
    label: string;
    icon: Component;
    sources: RequestKindSource[];
  }
</script>

<script lang="ts">
  import { ChevronRight } from "@lucide/svelte";
  import { Button, cn } from "@prismedia/ui-svelte";
  import PluginIcon from "$lib/components/plugins/PluginIcon.svelte";
  import { bandGradient, type MediaFamily } from "$lib/entities/media-families";

  interface Props {
    family: MediaFamily;
    kinds: RequestFamilyKind[];
    /** False while sources are still loading. */
    ready: boolean;
    onChoose: (kind: string) => void;
  }

  let { family, kinds, ready, onChoose }: Props = $props();
  const Icon = $derived(family.icon);
</script>

<!-- A media family as Request sees it: the kinds you can ask for and the sources that can find them. -->
<article
  class="relative flex min-w-0 flex-col overflow-hidden rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-2)] shadow-[var(--shadow-card)]"
  aria-label={family.label}
>
  <span class="absolute inset-x-0 top-0 h-[2px]" style:background={bandGradient(family.accent)} aria-hidden="true"></span>
  <header class="flex items-center gap-3 px-4 pt-4">
    <span class="grid size-9 shrink-0 place-items-center rounded-[var(--radius-sm)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] text-text-secondary">
      <Icon class="size-4" aria-hidden="true" />
    </span>
    <h3 class="min-w-0 flex-1 truncate font-heading text-sm font-semibold text-text-primary">{family.label}</h3>
  </header>
  <ul class="flex flex-col px-2 pt-3 pb-2">
    {#each kinds as entry (entry.kind)}
      {@const KindIcon = entry.icon}
      <li>
        <Button
          variant="ghost"
          size="sm"
          class={cn("h-auto w-full justify-between gap-3 px-2 py-2 font-normal", ready && entry.sources.length === 0 && "opacity-50")}
          onclick={() => onChoose(entry.kind)}
        >
          <span class="flex min-w-0 items-center gap-2.5">
            <KindIcon class="size-4 shrink-0 text-text-muted" aria-hidden="true" />
            <span class="truncate text-sm text-text-primary">{entry.label}</span>
          </span>
          <span class="flex shrink-0 items-center gap-1.5">
            {#each entry.sources.slice(0, 4) as source (source.id)}
              <span title={source.name}><PluginIcon name={source.name} iconUrl={source.iconUrl} class="size-5" /></span>
            {/each}
            {#if entry.sources.length > 4}
              <span class="font-mono text-[0.64rem] text-text-muted">+{entry.sources.length - 4}</span>
            {/if}
            <ChevronRight class="size-3.5 text-text-muted" aria-hidden="true" />
          </span>
        </Button>
      </li>
    {/each}
  </ul>
</article>
