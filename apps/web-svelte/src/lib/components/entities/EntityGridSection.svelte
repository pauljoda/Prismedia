<script lang="ts">
  import { browser } from "$app/environment";
  import { ChevronDown } from "@lucide/svelte";
  import type { Component, Snippet } from "svelte";

  type IconComponent = Component<{ class?: string }>;

  interface Props {
    children: Snippet;
    count: number;
    icon?: IconComponent;
    prefsKey: string;
    title: string;
  }

  let {
    children,
    count,
    icon: Icon,
    prefsKey,
    title,
  }: Props = $props();

  // svelte-ignore state_referenced_locally
  const storageKey = `prismedia:entity-grid-section:${prefsKey}`;
  // svelte-ignore state_referenced_locally
  const contentId = `entity-grid-section-${slugify(prefsKey)}`;
  let collapsed = $state(readStoredCollapsed(storageKey));

  function readStoredCollapsed(key: string): boolean {
    if (!browser) return false;
    try {
      return window.localStorage.getItem(key) === "collapsed";
    } catch {
      return false;
    }
  }

  function writeStoredCollapsed(next: boolean): void {
    if (!browser) return;
    try {
      window.localStorage.setItem(storageKey, next ? "collapsed" : "expanded");
    } catch {
      // Section collapse is a convenience preference; storage failures should not block interaction.
    }
  }

  function toggleCollapsed() {
    collapsed = !collapsed;
    writeStoredCollapsed(collapsed);
  }

  function slugify(value: string): string {
    return value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "") || "grid";
  }
</script>

<section class="content-section" class:is-collapsed={collapsed}>
  <button
    type="button"
    class="content-heading"
    aria-controls={contentId}
    aria-expanded={!collapsed}
    title={collapsed ? `Expand ${title}` : `Collapse ${title}`}
    onclick={toggleCollapsed}
  >
    <span class="heading-label">
      {#if Icon}
        <Icon class="h-4 w-4" />
      {/if}
      <span class="heading-title">{title}</span>
      <span class="content-count">{count}</span>
    </span>
    <span class="section-chevron" class:is-expanded={!collapsed} aria-hidden="true">
      <ChevronDown />
    </span>
  </button>

  {#if !collapsed}
    <div id={contentId} class="section-body">
      {@render children()}
    </div>
  {/if}
</section>

<style>
  .content-section {
    display: grid;
    gap: 0.75rem;
  }

  .content-heading {
    display: flex;
    min-width: 0;
    width: 100%;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    margin: 0;
    border: 1px solid transparent;
    border-radius: var(--radius-xs, 4px);
    background: transparent;
    color: var(--color-text-primary, #f2eed8);
    cursor: pointer;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 1.1rem;
    font-weight: 600;
    padding: 0.15rem 0.2rem;
    text-align: left;
    transition:
      background-color 160ms ease,
      border-color 160ms ease,
      color 160ms ease;
  }

  .content-heading:hover {
    border-color: var(--color-border-subtle, rgb(164 172 185 / 0.07));
    background: color-mix(in srgb, var(--color-surface-2, #11161d) 46%, transparent);
  }

  .content-heading:focus-visible {
    outline: 1px solid rgb(199 201 204 / 0.72);
    outline-offset: 2px;
  }

  .heading-label {
    display: flex;
    min-width: 0;
    align-items: center;
    gap: 0.5rem;
  }

  .heading-title {
    overflow-wrap: anywhere;
  }

  .content-count {
    flex: 0 0 auto;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-muted, #8a93a6);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 600;
    padding: 0.1rem 0.4rem;
  }

  .section-chevron {
    display: inline-flex;
    width: 1rem;
    height: 1rem;
    flex: 0 0 auto;
    align-items: center;
    justify-content: center;
    color: var(--color-text-muted, #8a93a6);
    transition:
      color 160ms ease,
      transform 160ms ease;
  }

  .section-chevron :global(svg) {
    width: 1rem;
    height: 1rem;
  }

  .section-chevron.is-expanded {
    transform: rotate(180deg);
  }

  .content-heading:hover .section-chevron,
  .is-collapsed .section-chevron {
    color: var(--color-text-accent, #c7c9cc);
  }

  .section-body {
    min-width: 0;
  }
</style>
