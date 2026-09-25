<script module lang="ts">
  /** One identifiable kind inside the family. */
  export interface IdentifyFamilyKind {
    kind: string;
    label: string;
    /** Items with files that are not organized yet; null while counting. */
    unidentified: number | null;
    /** Items in the identify queue for this kind. */
    pending: number;
  }
</script>

<script lang="ts">
  import { ChevronRight } from "@lucide/svelte";
  import { Button, cn } from "@prismedia/ui-svelte";
  import type { PluginProvider } from "$lib/api/identify-types";
  import PluginIcon from "$lib/components/plugins/PluginIcon.svelte";
  import { bandGradient, type MediaFamily } from "$lib/entities/media-families";

  interface Props {
    family: MediaFamily;
    kinds: IdentifyFamilyKind[];
    providers: PluginProvider[];
    selectedKind: string | null;
    onOpenKind: (kind: string) => void;
  }

  let { family, kinds, providers, selectedKind, onOpenKind }: Props = $props();

  const numberFormat = new Intl.NumberFormat();
  const Icon = $derived(family.icon);
  const unidentified = $derived(kinds.reduce((sum, entry) => sum + (entry.unidentified ?? 0), 0));
  const counting = $derived(kinds.some((entry) => entry.unidentified === null));
</script>

<!-- A media family as Identify sees it: what is still unidentified, what is queued, and who can identify it. -->
<article
  class="relative flex min-w-0 flex-col overflow-hidden rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-2)] shadow-[var(--shadow-card)]"
  aria-label={family.label}
>
  <span class="absolute inset-x-0 top-0 h-[2px]" style:background={bandGradient(family.accent)} aria-hidden="true"></span>

  <header class="flex items-center gap-3 px-4 pt-4">
    <span
      class="grid size-9 shrink-0 place-items-center rounded-[var(--radius-sm)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] text-text-secondary"
    >
      <Icon class="size-4" aria-hidden="true" />
    </span>
    <h3 class="min-w-0 flex-1 truncate font-heading text-sm font-semibold text-text-primary">{family.label}</h3>
    <span class="text-right font-mono text-[0.68rem] leading-tight text-text-muted">
      <span class={cn("block text-base tabular-nums", unidentified > 0 ? "text-text-primary" : "text-text-disabled")}>
        {counting && unidentified === 0 ? "–" : numberFormat.format(unidentified)}
      </span>
      unidentified
    </span>
  </header>

  <ul class="flex flex-1 flex-col px-2 pt-3 pb-2">
    {#each kinds as entry (entry.kind)}
      <li>
        <Button
          variant="ghost"
          size="sm"
          class={cn(
            "h-auto w-full justify-between gap-3 px-2 py-1.5 font-normal",
            selectedKind === entry.kind && "bg-[var(--color-surface-3)]",
          )}
          aria-pressed={selectedKind === entry.kind}
          onclick={() => onOpenKind(entry.kind)}
        >
          <span class="min-w-0 truncate text-caption text-text-secondary">{entry.label}</span>
          <span class="flex shrink-0 items-center gap-3 font-mono text-[0.68rem] tabular-nums">
            {#if entry.pending > 0}
              <span class="text-text-primary">{entry.pending} queued</span>
            {/if}
            <span class={entry.unidentified ? "text-text-secondary" : "text-text-disabled"}>
              {entry.unidentified === null ? "–" : numberFormat.format(entry.unidentified)}
            </span>
            <ChevronRight class="size-3.5 text-text-muted" aria-hidden="true" />
          </span>
        </Button>
      </li>
    {/each}
  </ul>

  <footer class="flex items-center gap-2 border-t border-[var(--color-border-subtle)] px-4 py-2">
    <ul class="flex min-w-0 flex-1 items-center gap-1.5" aria-label="Providers for {family.label}">
      {#each providers as provider (provider.id)}
        <li title={provider.name}><PluginIcon name={provider.name} iconUrl={provider.iconUrl} class="size-6" /></li>
      {/each}
    </ul>
    <span class="font-mono text-[0.64rem] text-text-disabled">{providers.length} {providers.length === 1 ? "provider" : "providers"}</span>
  </footer>
</article>
