<script lang="ts">
  import { ExternalLink, Fingerprint, Link } from "@lucide/svelte";
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
                <span class="provider-id-provider">{link.provider}</span>
                <span class="provider-id-value">{externalValue}</span>
              </a>
            {:else}
              <span class="link-item provider-id-item no-url">
                <Fingerprint class="h-3.5 w-3.5" />
                <span class="provider-id-provider">{link.provider}</span>
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
    gap: 0.4rem;
  }

  .link-group {
    display: grid;
    gap: 0.4rem;
  }

  .link-group + .link-group {
    margin-top: 0.9rem;
  }

  .link-group-label {
    color: var(--color-text-muted, #7d8596);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
    font-weight: 700;
    letter-spacing: 0;
    text-transform: uppercase;
  }

  .link-item {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    min-width: 0;
    padding: 0.3rem 0.55rem;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.07));
    border-radius: var(--radius-xs, 4px);
    background: linear-gradient(135deg, rgba(35, 42, 58, 0.9), rgba(18, 23, 34, 0.92));
    color: var(--color-text-secondary, #c4c9d4);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.74rem;
    text-decoration: none;
    overflow: hidden;
    transition: border-color 0.15s, color 0.15s, background 0.15s;
  }

  a.link-item:hover {
    color: var(--detail-accent);
    border-color: var(--detail-accent-muted);
    background: linear-gradient(135deg, rgba(48, 43, 33, 0.96), rgba(22, 24, 32, 0.94));
  }

  .url-link-item {
    gap: 0.55rem;
    min-height: 3.25rem;
    padding: 0.55rem 0.65rem;
  }

  .url-link-icon {
    display: grid;
    flex: 0 0 auto;
    place-items: center;
    width: 1.55rem;
    height: 1.55rem;
    border: 1px solid rgba(199, 201, 204, 0.18);
    border-radius: var(--radius-xs, 4px);
    background: rgba(199, 201, 204, 0.07);
    color: var(--detail-accent);
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
    font-size: 0.82rem;
    font-weight: 650;
    letter-spacing: 0.01em;
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

  .provider-id-provider {
    color: var(--detail-accent);
    font-weight: 800;
    text-transform: uppercase;
  }

  .provider-id-value {
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .link-item.no-url {
    color: var(--color-text-muted, #8a93a6);
  }
</style>
