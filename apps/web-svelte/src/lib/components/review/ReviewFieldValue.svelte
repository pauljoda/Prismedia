<script lang="ts">
  import { ExternalLink } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import type { EntityMetadataProposal } from "$lib/api/identify-types";
  import { METADATA_PATCH_FIELD } from "$lib/entities/entity-codes";
  import { proposalFieldValue, readableProviderKey, reviewDateTypeLabel } from "$lib/components/identify-review";

  interface Props {
    proposal: EntityMetadataProposal;
    field: string;
  }

  let { proposal, field }: Props = $props();

  let expanded = $state(false);
  const patch = $derived(proposal.patch);
  const dateFormat = new Intl.DateTimeFormat(undefined, { year: "numeric", month: "short", day: "numeric" });
  const numberFormat = new Intl.NumberFormat();

  /** Dates read as "5 Feb 2019"; partial values such as a bare year stay as written. */
  function formatDate(value: string): string {
    if (!/^\d{4}-\d{2}-\d{2}/.test(value)) return value;
    const parsed = new Date(`${value.slice(0, 10)}T00:00:00`);
    return Number.isNaN(parsed.getTime()) ? value : dateFormat.format(parsed);
  }

  function hostname(url: string): string {
    try {
      return new URL(url).hostname.replace(/^www\./, "");
    } catch {
      return url;
    }
  }

  /** Typed entries and the legacy date map can carry the same date; each reads once. */
  const dates = $derived.by(() => {
    const seen = new Set<string>();
    return [
      ...(patch.dateEntries ?? []).map((entry) => ({ label: reviewDateTypeLabel(entry.type), value: entry.value })),
      ...Object.entries(patch.dates ?? {}).map(([key, value]) => ({ label: reviewDateTypeLabel(key), value })),
    ].filter((date) => {
      const key = `${date.label}:${date.value}`;
      if (seen.has(key)) return false;
      seen.add(key);
      return true;
    });
  });
</script>

{#if field === METADATA_PATCH_FIELD.description}
  <div class="flex flex-col items-start gap-1">
    <p class={expanded ? "whitespace-pre-line" : "line-clamp-4 whitespace-pre-line"}>{patch.description}</p>
    {#if (patch.description?.length ?? 0) > 280}
      <Button variant="ghost" size="xs" class="-ml-2 text-text-muted" onclick={() => (expanded = !expanded)}>
        {expanded ? "Less" : "More"}
      </Button>
    {/if}
  </div>
{:else if field === METADATA_PATCH_FIELD.externalIds}
  <ul class="flex flex-wrap gap-1.5">
    {#each Object.entries(patch.externalIds ?? {}) as [namespace, value] (namespace)}
      <li class="rounded-[var(--radius-xs)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] px-2 py-0.5 font-mono text-[0.68rem]">
        <span class="text-text-muted">{namespace}</span> <span class="text-text-primary">{value}</span>
      </li>
    {/each}
    {#each patch.retiredExternalIds ?? [] as identity (`${identity.namespace}:${identity.value}`)}
      <li class="rounded-[var(--radius-xs)] border border-dashed border-[var(--color-border-subtle)] px-2 py-0.5 font-mono text-[0.68rem] text-text-disabled line-through">
        {identity.namespace} {identity.value}
      </li>
    {/each}
  </ul>
{:else if field === METADATA_PATCH_FIELD.urls}
  <ul class="flex flex-wrap gap-1.5">
    {#each [...new Set(patch.urls)] as url (url)}
      <li>
        <a
          href={url}
          target="_blank"
          rel="noreferrer"
          class="inline-flex items-center gap-1 rounded-[var(--radius-xs)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] px-2 py-0.5 text-caption text-text-secondary transition-colors hover:text-text-primary"
          title={url}
        >
          {hostname(url)}
          <ExternalLink class="size-3 text-text-muted" aria-hidden="true" />
        </a>
      </li>
    {/each}
  </ul>
{:else if field === METADATA_PATCH_FIELD.dates}
  <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-0.5">
    {#each dates as date (`${date.label}:${date.value}`)}
      <dt class="text-caption text-text-muted">{date.label}</dt>
      <dd class="tabular-nums">{formatDate(date.value)}</dd>
    {/each}
  </dl>
{:else if field === METADATA_PATCH_FIELD.stats}
  <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-0.5">
    {#each Object.entries(patch.stats ?? {}) as [key, value] (key)}
      <dt class="text-caption text-text-muted">{readableProviderKey(key)}</dt>
      <dd class="tabular-nums">{numberFormat.format(value)}</dd>
    {/each}
  </dl>
{:else}
  <p>{proposalFieldValue(proposal, field)}</p>
{/if}
