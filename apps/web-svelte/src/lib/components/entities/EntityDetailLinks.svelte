<script lang="ts">
  import { ExternalLink, Fingerprint, Link } from "@lucide/svelte";
  import { Item } from "@prismedia/ui-svelte";
  import MetadataCard from "$lib/components/MetadataCard.svelte";
  import type { EntityDetailLink } from "$lib/entities/entity-detail";
  import { groupEntityLinks, linkHostname, type EntityLinkRow } from "$lib/entities/entity-detail-links";

  let { links }: { links: EntityDetailLink[] } = $props();
  const groups = $derived(groupEntityLinks(links));
</script>

{#snippet linkContent(row: EntityLinkRow)}
  <Item.Media variant="icon" class="self-start">
    {#if row.identifiers.length}<Fingerprint />{:else}<Link />{/if}
  </Item.Media>
  <Item.Content class="min-w-0">
    <Item.Title>
      {row.link.url ? linkHostname(row.link) : row.link.provider ?? row.link.label}
    </Item.Title>
    {#if row.identifiers.length}
      <dl class="grid min-w-0 grid-cols-[minmax(0,max-content)_minmax(0,1fr)] gap-x-3 gap-y-1 text-sm">
        {#each row.identifiers as id (JSON.stringify([id.provider, id.value]))}
          <dt class="break-all text-muted-foreground">{id.provider}</dt>
          <dd class="min-w-0 break-all">{id.value}</dd>
        {/each}
      </dl>
    {:else}
      <Item.Description class="break-all line-clamp-none">{row.link.url ?? row.link.label}</Item.Description>
    {/if}
  </Item.Content>
  {#if row.link.url}
    <Item.Actions><ExternalLink class="size-4 text-muted-foreground" aria-hidden="true" /></Item.Actions>
  {/if}
{/snippet}

{#if links.length > 0}
  <MetadataCard title="Links & Provider IDs" icon={Link} wide capped>
    <div class="flex min-w-0 flex-col gap-4">
      {#each groups as group (group.label)}
        <section aria-label={group.label} class="min-w-0">
          {#if groups.length > 1}
            <h4 class="mb-2 text-xs font-medium text-muted-foreground">{group.label}</h4>
          {/if}
          <Item.Group>
            {#each group.rows as row (row.key)}
              <div role="listitem">
                <Item.Root size="sm" variant="muted">
                  {#snippet child({ props })}
                    {#if row.link.url}
                      <a {...props} href={row.link.url} target="_blank" rel="noopener noreferrer">
                        {@render linkContent(row)}
                      </a>
                    {:else}
                      <div {...props}>{@render linkContent(row)}</div>
                    {/if}
                  {/snippet}
                </Item.Root>
              </div>
            {/each}
          </Item.Group>
        </section>
      {/each}
    </div>
  </MetadataCard>
{/if}
