<script lang="ts">
  import { ExternalLink, Fingerprint, Link } from "@lucide/svelte";
  import { Item } from "@prismedia/ui-svelte";
  import MetadataCard from "$lib/components/MetadataCard.svelte";
  import type { EntityDetailLink } from "$lib/entities/entity-detail";
  import { externalIdValue } from "$lib/entities/entity-detail-edit";
  import { groupEntityLinks, linkHostname } from "$lib/entities/entity-detail-links";

  let { links }: { links: EntityDetailLink[] } = $props();
  const groups = $derived(groupEntityLinks(links));
</script>

{#snippet linkContent(link: EntityDetailLink)}
  <Item.Media variant="icon">
    {#if link.provider}<Fingerprint />{:else}<Link />{/if}
  </Item.Media>
  <Item.Content class="min-w-0">
    <Item.Title>
      {#if link.provider}<span class="uppercase">{link.provider}</span>{:else}{linkHostname(link)}{/if}
    </Item.Title>
    <Item.Description class="break-all">
      {link.provider ? externalIdValue(link.label, link.provider) : link.url ?? link.label}
    </Item.Description>
  </Item.Content>
  {#if link.url}
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
            {#each group.links as link (`${link.provider ?? ""}:${link.label}:${link.url ?? ""}`)}
              <div role="listitem">
                <Item.Root size="sm" variant="muted">
                  {#snippet child({ props })}
                    {#if link.url}
                      <a {...props} href={link.url} target="_blank" rel="noopener noreferrer">
                        {@render linkContent(link)}
                      </a>
                    {:else}
                      <div {...props}>{@render linkContent(link)}</div>
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
