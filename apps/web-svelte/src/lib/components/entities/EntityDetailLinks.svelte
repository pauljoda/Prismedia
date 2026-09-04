<script lang="ts">
  import { ExternalLink, Fingerprint, Link } from "@lucide/svelte";
  import { Badge } from "@prismedia/ui-svelte";
  import MetadataCard from "$lib/components/MetadataCard.svelte";
  import type { EntityDetailLink } from "$lib/entities/entity-detail";
  import { externalIdValue, hasProvider } from "$lib/entities/entity-detail-edit";

  interface Props {
    links: EntityDetailLink[];
  }

  let { links }: Props = $props();

  const urlLinks = $derived(links.filter((link) => !hasProvider(link)));
  const providerIdLinks = $derived(links.filter(hasProvider));

  function urlLinkTitle(link: EntityDetailLink): string {
    const parsed = parseUrl(link.url ?? link.label);
    if (!parsed) return link.label;
    return parsed.hostname.replace(/^www\./i, "");
  }

  function urlLinkSubtitle(link: EntityDetailLink): string {
    return link.url ?? link.label;
  }

  function parseUrl(value: string): URL | null {
    try {
      return new URL(value);
    } catch {
      return null;
    }
  }
</script>

{#if links.length > 0}
  <MetadataCard title="Links & Provider IDs" icon={Link} wide capped>
    {#if urlLinks.length > 0}
      <div class="link-group">
        <div class="link-group-label">URLs</div>
        <div class="link-list">
          {#each urlLinks as link (link.label)}
            {@const title = urlLinkTitle(link)}
            {@const subtitle = urlLinkSubtitle(link)}
            {#if link.url}
              <a href={link.url} target="_blank" rel="noopener noreferrer" class="link-item url-link-item" title={subtitle}>
                <span class="url-link-icon" aria-hidden="true">
                  <ExternalLink class="h-3.5 w-3.5" />
                </span>
                <span class="url-link-copy">
                  <span class="url-link-title">{title}</span>
                  <span class="url-link-subtitle">{subtitle}</span>
                </span>
              </a>
            {:else}
              <span class="link-item url-link-item no-url" title={subtitle}>
                <span class="url-link-icon" aria-hidden="true">
                  <Link class="h-3.5 w-3.5" />
                </span>
                <span class="url-link-copy">
                  <span class="url-link-title">{title}</span>
                  <span class="url-link-subtitle">{subtitle}</span>
                </span>
              </span>
            {/if}
          {/each}
        </div>
      </div>
    {/if}
    {#if providerIdLinks.length > 0}
      <div class="link-group">
        <div class="link-group-label">Provider IDs</div>
        <div class="link-list">
          {#each providerIdLinks as link (`${link.provider}:${externalIdValue(link.label, link.provider)}`)}
            {@const externalValue = externalIdValue(link.label, link.provider)}
            {#if link.url}
              <a href={link.url} target="_blank" rel="noopener noreferrer" class="link-item provider-id-item">
                <ExternalLink class="h-3.5 w-3.5" />
                <Badge class="text-[0.6875rem] uppercase">{link.provider}</Badge>
                <span class="provider-id-value">{externalValue}</span>
              </a>
            {:else}
              <span class="link-item provider-id-item no-url">
                <Fingerprint class="h-3.5 w-3.5" />
                <Badge class="text-[0.6875rem] uppercase">{link.provider}</Badge>
                <span class="provider-id-value">{externalValue}</span>
              </span>
            {/if}
          {/each}
        </div>
      </div>
    {/if}
  </MetadataCard>
{/if}

<style>
  .link-list {
    display: grid;
    gap: 0.5rem;
  }

  .link-group {
    display: grid;
    gap: 0.5rem;
  }

  .link-group + .link-group {
    margin-top: 0.9rem;
  }

  .link-group-label {
    color: var(--color-text-muted, #7d8596);
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.75rem;
    font-weight: 600;
  }

  .link-item {
    display: flex;
    align-items: center;
    gap: 0.55rem;
    min-width: 0;
    min-height: 2.5rem;
    padding: 0.5rem 0.65rem;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.07));
    border-radius: var(--radius-sm, 6px);
    background: var(--color-surface-1, #0c0f15);
    color: var(--color-text-secondary, #c4c9d4);
    font-family: var(--font-body, Inter, sans-serif);
    font-size: 0.8125rem;
    text-decoration: none;
    overflow: hidden;
    transition: border-color 0.15s, color 0.15s, background 0.15s;
  }

  a.link-item:hover {
    color: var(--detail-text);
    border-color: var(--color-border-default);
    background: var(--color-surface-2, #11161d);
  }

  .url-link-item {
    gap: 0.55rem;
    min-height: 3rem;
  }

  .url-link-icon {
    display: grid;
    flex: 0 0 auto;
    place-items: center;
    width: 1.75rem;
    height: 1.75rem;
    border-radius: var(--radius-sm, 6px);
    background: var(--color-surface-3, #171c25);
    color: var(--color-text-muted, #8a93a6);
  }

  .url-link-copy {
    display: grid;
    gap: 0.08rem;
    min-width: 0;
  }

  .url-link-title,
  .url-link-subtitle {
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .url-link-title {
    color: var(--detail-text, #f2eed8);
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.875rem;
    font-weight: 600;
  }

  .url-link-subtitle {
    color: var(--color-text-muted, #8a93a6);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
  }

  .provider-id-item {
    gap: 0.5rem;
    white-space: nowrap;
  }

  .provider-id-item :global(svg) {
    flex: 0 0 auto;
  }

  .provider-id-value {
    min-width: 0;
    overflow: hidden;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.75rem;
    text-overflow: ellipsis;
  }

  .link-item.no-url {
    color: var(--color-text-muted, #8a93a6);
  }
</style>
