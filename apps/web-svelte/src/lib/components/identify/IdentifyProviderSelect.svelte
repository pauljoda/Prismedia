<script lang="ts">
  import { onMount, tick } from "svelte";
  import { ChevronDown, Search } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import type { PluginProvider } from "$lib/api/identify-types";

  interface Props {
    providers: PluginProvider[];
    selectedId: string;
    onChange: (providerId: string) => void;
    label?: string;
    compact?: boolean;
  }

  let {
    providers,
    selectedId,
    onChange,
    label = "Provider",
    compact = false,
  }: Props = $props();

  let open = $state(false);
  let query = $state("");
  let activeIndex = $state(0);
  let container: HTMLDivElement | null = $state(null);
  let searchInput: HTMLInputElement | null = $state(null);

  const selectedProvider = $derived(
    providers.find((provider) => provider.id === selectedId) ?? providers[0] ?? null,
  );
  const queryLower = $derived(query.trim().toLowerCase());
  const filteredProviders = $derived(
    queryLower
      ? providers.filter((provider) =>
          provider.name.toLowerCase().includes(queryLower) ||
          provider.id.toLowerCase().includes(queryLower),
        )
      : providers,
  );
  const visibleProviders = $derived(filteredProviders.slice(0, 50));
  const buttonLabel = $derived(selectedProvider?.name ?? "Select provider");
  const id = `provider-select-${Math.random().toString(36).slice(2, 9)}`;

  async function openPanel() {
    if (providers.length === 0) return;
    open = true;
    query = "";
    activeIndex = Math.max(0, visibleProviders.findIndex((provider) => provider.id === selectedProvider?.id));
    await tick();
    searchInput?.focus();
  }

  function closePanel() {
    open = false;
    query = "";
    activeIndex = 0;
  }

  function commit(providerId: string) {
    onChange(providerId);
    closePanel();
  }

  function handleKeyDown(event: KeyboardEvent) {
    if (event.key === "Escape") {
      event.preventDefault();
      closePanel();
      return;
    }
    if (event.key === "ArrowDown") {
      event.preventDefault();
      activeIndex = Math.min(activeIndex + 1, Math.max(0, visibleProviders.length - 1));
      return;
    }
    if (event.key === "ArrowUp") {
      event.preventDefault();
      activeIndex = Math.max(activeIndex - 1, 0);
      return;
    }
    if (event.key === "Enter") {
      event.preventDefault();
      const provider = visibleProviders[activeIndex];
      if (provider) commit(provider.id);
    }
  }

  onMount(() => {
    function handleClick(event: MouseEvent) {
      if (open && container && !container.contains(event.target as Node)) {
        closePanel();
      }
    }

    document.addEventListener("mousedown", handleClick);
    return () => document.removeEventListener("mousedown", handleClick);
  });
</script>

<div bind:this={container} class={cn("provider-select", compact && "is-compact")}>
  <button
    {id}
    type="button"
    class={cn("provider-trigger", open && "is-open")}
    aria-label={`${label}: ${buttonLabel}`}
    aria-haspopup="listbox"
    aria-expanded={open}
    onclick={() => {
      if (open) closePanel();
      else void openPanel();
    }}
  >
    <span class="provider-trigger-copy">
      <span class="provider-label">{label}</span>
      <span class="provider-name">{buttonLabel}</span>
    </span>
    <ChevronDown class={cn("provider-chevron", open && "rotate-180")} />
  </button>

  {#if open}
    <div class="provider-menu">
      <div class="provider-search-row">
        <Search class="h-3.5 w-3.5 shrink-0 text-text-disabled" />
        <input
          bind:this={searchInput}
          bind:value={query}
          class="provider-search"
          placeholder="Search providers…"
          aria-label="Search providers"
          oninput={() => (activeIndex = 0)}
          onkeydown={handleKeyDown}
        />
      </div>
      <div class="provider-list" role="listbox" aria-labelledby={id}>
        {#if visibleProviders.length === 0}
          <p class="provider-empty">No providers found</p>
        {/if}
        {#each visibleProviders as provider, index (provider.id)}
          {@const selected = provider.id === selectedProvider?.id}
          {@const active = index === activeIndex}
          <button
            type="button"
            role="option"
            aria-selected={selected}
            class={cn("provider-option", active && "is-active", selected && "is-selected")}
            onmouseenter={() => (activeIndex = index)}
            onmousedown={(event) => {
              event.preventDefault();
              commit(provider.id);
            }}
          >
            <span class="provider-option-copy">
              <span class="provider-option-name">{provider.name}</span>
              <span class="provider-option-id">{provider.id}</span>
            </span>
          </button>
        {/each}
      </div>
    </div>
  {/if}
</div>

<style>
  .provider-select {
    position: relative;
    width: min(22rem, 100%);
  }

  .provider-select.is-compact {
    width: min(17rem, 100%);
  }

  .provider-trigger {
    display: flex;
    width: 100%;
    min-height: 2.25rem;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-radius: var(--radius-xs, 4px);
    background: var(--color-surface-2, #11151c);
    box-shadow: inset 0 2px 8px rgba(0, 0, 0, 0.3);
    color: var(--color-text-primary, #f5f2ea);
    padding: 0.35rem 0.6rem;
    text-align: left;
    transition:
      border-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      box-shadow var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      background var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .provider-trigger:hover,
  .provider-trigger.is-open {
    border-color: var(--color-border-accent, rgba(199, 155, 92, 0.24));
    background: var(--color-surface-3, #181d27);
    box-shadow:
      inset 0 2px 8px rgba(0, 0, 0, 0.3),
      0 0 0 1px rgba(242, 194, 106, 0.28),
      0 0 8px rgba(242, 194, 106, 0.13);
  }

  .provider-trigger-copy {
    display: flex;
    min-width: 0;
    flex: 1;
    align-items: baseline;
    gap: 0.45rem;
  }

  .provider-label {
    flex-shrink: 0;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
    letter-spacing: 0;
    text-transform: uppercase;
    color: var(--color-text-disabled, #5a6070);
  }

  .provider-name {
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.74rem;
    color: var(--color-text-accent, #c79b5c);
  }

  .provider-chevron {
    height: 0.9rem;
    width: 0.9rem;
    flex-shrink: 0;
    color: var(--color-text-muted, #a4acb9);
    transition: transform 0.15s ease;
  }

  .provider-menu {
    position: absolute;
    left: 0;
    right: 0;
    top: calc(100% + 0.25rem);
    z-index: 60;
    overflow: hidden;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-radius: var(--radius-sm, 6px);
    background: rgba(12, 15, 21, 0.98);
    box-shadow: 0 8px 40px rgba(0, 0, 0, 0.6);
    backdrop-filter: blur(var(--glass-blur-lg));
    -webkit-backdrop-filter: blur(var(--glass-blur-lg));
  }

  .provider-search-row {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    border-bottom: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    background: var(--color-surface-1, #0c0f15);
    padding: 0.55rem 0.65rem;
  }

  .provider-search {
    min-width: 0;
    width: 100%;
    border: 0;
    background: transparent;
    color: var(--color-text-primary, #f5f2ea);
    font-size: 0.82rem;
    outline: none;
  }

  .provider-search::placeholder {
    color: var(--color-text-disabled, #5a6070);
  }

  .provider-list {
    max-height: 18rem;
    overflow-y: auto;
    padding: 0.25rem;
    scrollbar-width: thin;
  }

  .provider-empty {
    margin: 0;
    padding: 0.85rem;
    text-align: center;
    font-size: 0.78rem;
    color: var(--color-text-disabled, #5a6070);
  }

  .provider-option {
    display: flex;
    width: 100%;
    align-items: center;
    gap: 0.5rem;
    border: 0;
    border-radius: var(--radius-xs, 4px);
    background: transparent;
    padding: 0.5rem 0.55rem;
    text-align: left;
    color: var(--color-text-secondary, #c8ccd4);
    transition:
      background var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .provider-option.is-active {
    background: var(--color-surface-3, #181d27);
    color: var(--color-text-primary, #f5f2ea);
  }

  .provider-option.is-selected {
    color: var(--color-text-accent, #c79b5c);
  }

  .provider-option-copy {
    display: flex;
    min-width: 0;
    flex: 1;
    flex-direction: column;
    gap: 0.05rem;
  }

  .provider-option-name,
  .provider-option-id {
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .provider-option-name {
    font-size: 0.82rem;
  }

  .provider-option-id {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.66rem;
    color: var(--color-text-disabled, #5a6070);
  }
</style>
