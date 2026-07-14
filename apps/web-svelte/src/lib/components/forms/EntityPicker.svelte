<script lang="ts">
  import { onMount, tick, type Component } from "svelte";
  import { Plus, Search, X } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { keepFlyoutOnScreen } from "$lib/actions/keep-flyout-on-screen";
  import FormField from "./FormField.svelte";

  export interface EntityPickerItem {
    id: string;
    title: string;
    thumbnailUrl: string | null;
    subtitle?: string;
  }

  interface Props {
    /** Selected entity IDs (multi) or titles (tags). */
    values: EntityPickerItem[];
    onChange: (values: EntityPickerItem[]) => void;
    /** Async search function called on query change. */
    onSearch: (query: string) => Promise<EntityPickerItem[]>;
    label?: string;
    icon?: Component;
    placeholder?: string;
    helper?: string;
    error?: string;
    disabled?: boolean;
    /** Allow creating new items when no match is found. */
    canAddNew?: boolean;
    /** Label for new items, e.g. "tag" → 'Add "foo" as new tag'. */
    addNewLabel?: string;
    /** Multi-select (tags) or single-select (studio). */
    mode?: "multi" | "single";
    maxResults?: number;
    /**
     * Renders the selected items as inline chips (default). Hosts that present
     * selections themselves (e.g. the credits editor's per-person rows) turn this
     * off and use the picker purely as a search-and-add control.
     */
    showSelectedChips?: boolean;
  }

  let {
    values,
    onChange,
    onSearch,
    label,
    icon,
    placeholder = "Search…",
    helper,
    error,
    disabled = false,
    canAddNew = false,
    addNewLabel = "item",
    mode = "multi",
    maxResults = 20,
    showSelectedChips = true,
  }: Props = $props();

  let query = $state("");
  let open = $state(false);
  let activeIndex = $state(0);
  let results = $state<EntityPickerItem[]>([]);
  let searching = $state(false);
  let container: HTMLDivElement | null = $state(null);
  let inputEl: HTMLInputElement | null = $state(null);
  let searchTimer: ReturnType<typeof setTimeout> | null = null;

  const trimmed = $derived(query.trim());
  const queryLower = $derived(trimmed.toLowerCase());
  const selectedIds = $derived(new Set(values.map((v) => v.id)));

  const available = $derived(
    results.filter((r) => !selectedIds.has(r.id)),
  );
  const limited = $derived(available.slice(0, maxResults));
  const hasExactMatch = $derived(
    !!trimmed && results.some((r) => r.title.toLowerCase() === queryLower),
  );
  const alreadySelected = $derived(
    !!trimmed && values.some((v) => v.title.toLowerCase() === queryLower),
  );
  const showAddOption = $derived(
    canAddNew && !!trimmed && !hasExactMatch && !alreadySelected,
  );
  const totalItems = $derived(limited.length + (showAddOption ? 1 : 0));

  const id = `picker-${Math.random().toString(36).slice(2, 9)}`;

  function doSearch(q: string) {
    if (searchTimer) clearTimeout(searchTimer);
    if (!q.trim()) {
      searchTimer = setTimeout(async () => {
        searching = true;
        try {
          results = await onSearch("");
        } finally {
          searching = false;
        }
      }, 0);
      return;
    }
    searchTimer = setTimeout(async () => {
      searching = true;
      try {
        results = await onSearch(q.trim());
      } finally {
        searching = false;
      }
    }, 200);
  }

  function addItem(item: EntityPickerItem) {
    if (mode === "single") {
      onChange([item]);
    } else {
      onChange([...values, item]);
    }
    query = "";
    activeIndex = 0;
    results = [];
    if (mode === "single") open = false;
  }

  function addNew(title: string) {
    addItem({
      id: `new:${title.toLowerCase()}`,
      title,
      thumbnailUrl: null,
    });
  }

  function removeAt(index: number) {
    onChange(values.filter((_, i) => i !== index));
  }

  async function openPanel() {
    open = true;
    await tick();
    inputEl?.focus();
    doSearch("");
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (e.key === "Enter") {
      e.preventDefault();
      if (activeIndex < limited.length) {
        const opt = limited[activeIndex];
        if (opt) addItem(opt);
      } else if (showAddOption) {
        addNew(trimmed);
      }
      return;
    }
    if (e.key === "Backspace" && query === "" && values.length > 0 && mode === "multi" && showSelectedChips) {
      removeAt(values.length - 1);
      return;
    }
    if (e.key === "ArrowDown") {
      e.preventDefault();
      open = true;
      activeIndex = Math.min(activeIndex + 1, Math.max(0, totalItems - 1));
      return;
    }
    if (e.key === "ArrowUp") {
      e.preventDefault();
      activeIndex = Math.max(activeIndex - 1, 0);
      return;
    }
    if (e.key === "Escape") {
      open = false;
    }
  }

  onMount(() => {
    function handleClick(e: MouseEvent) {
      if (open && container && !container.contains(e.target as Node)) {
        open = false;
      }
    }
    document.addEventListener("mousedown", handleClick);
    return () => {
      document.removeEventListener("mousedown", handleClick);
      if (searchTimer) clearTimeout(searchTimer);
    };
  });
</script>

<FormField {label} {icon} {helper} {error} htmlFor={id}>
  <div bind:this={container} class="relative">
    <!-- Selected chips + search input -->
    <div
      class={cn(
        "picker-input-area",
        open && "is-focused",
        disabled && "is-disabled",
        error && "is-error",
      )}
      role="button"
      tabindex="-1"
      onfocusin={() => {
        if (!open) openPanel();
      }}
      onkeydown={() => {}}
    >
      {#if !showSelectedChips}
        <!-- Host renders selections; the picker is search-and-add only. -->
      {:else if mode === "multi"}
        {#each values as item, i (item.id)}
          <span class="picker-chip">
            {#if item.thumbnailUrl}
              <img src={item.thumbnailUrl} alt="" class="chip-avatar" />
            {/if}
            <span class="truncate">{item.title}</span>
            <button
              type="button"
              onclick={(e) => {
                e.stopPropagation();
                removeAt(i);
              }}
              aria-label={`Remove ${item.title}`}
              class="chip-remove"
            >
              <X class="h-2.5 w-2.5" />
            </button>
          </span>
        {/each}
      {:else if values.length > 0}
        <span class="picker-chip single">
          {#if values[0].thumbnailUrl}
            <img src={values[0].thumbnailUrl} alt="" class="chip-avatar" />
          {/if}
          <span class="truncate">{values[0].title}</span>
          <button
            type="button"
            onclick={(e) => {
              e.stopPropagation();
              onChange([]);
            }}
            aria-label={`Clear ${values[0].title}`}
            class="chip-remove"
          >
            <X class="h-2.5 w-2.5" />
          </button>
        </span>
      {/if}

      <input
        {id}
        bind:this={inputEl}
        bind:value={query}
        oninput={() => {
          open = true;
          activeIndex = 0;
          doSearch(query);
        }}
        onfocus={() => {
          if (!open) openPanel();
        }}
        onkeydown={handleKeyDown}
        {placeholder}
        {disabled}
        class="picker-search-inline"
        aria-autocomplete="list"
        aria-expanded={open}
      />
    </div>

    <!-- Dropdown -->
    {#if open}
      <div class="floating-surface picker-dropdown" use:keepFlyoutOnScreen>
        {#if searching && limited.length === 0}
          <p class="picker-empty">Searching…</p>
        {:else if limited.length === 0 && !showAddOption}
          <p class="picker-empty">
            {trimmed ? `No ${addNewLabel}s found` : `Type to search ${addNewLabel}s`}
          </p>
        {/if}

        <div role="listbox" class="picker-list">
          {#each limited as item, i (item.id)}
            {@const active = i === activeIndex}
            {@const selected = selectedIds.has(item.id)}
            <button
              type="button"
              role="option"
              aria-selected={selected}
              onmouseenter={() => (activeIndex = i)}
              onmousedown={(e) => {
                e.preventDefault();
                addItem(item);
              }}
              class={cn("picker-option", active && "is-active", selected && "is-selected")}
            >
              <div class="option-avatar">
                {#if item.thumbnailUrl}
                  <img src={item.thumbnailUrl} alt="" />
                {:else}
                  <div class="avatar-placeholder">
                    {item.title.charAt(0).toUpperCase()}
                  </div>
                {/if}
              </div>
              <div class="option-text">
                <span class="option-title">{item.title}</span>
                {#if item.subtitle}
                  <span class="option-subtitle">{item.subtitle}</span>
                {/if}
              </div>
            </button>
          {/each}

          {#if showAddOption}
            {@const addIdx = limited.length}
            {@const addActive = addIdx === activeIndex}
            <button
              type="button"
              role="option"
              aria-selected={addActive}
              onmouseenter={() => (activeIndex = addIdx)}
              onmousedown={(e) => {
                e.preventDefault();
                addNew(trimmed);
              }}
              class={cn("picker-option picker-add", addActive && "is-active")}
            >
              <div class="option-avatar add-avatar">
                <Plus class="h-3.5 w-3.5" />
              </div>
              <div class="option-text">
                <span class="option-title add-title">Add "{trimmed}"</span>
                <span class="option-subtitle">Create new {addNewLabel}</span>
              </div>
            </button>
          {/if}
        </div>
      </div>
    {/if}
  </div>
</FormField>

<style>
  .picker-input-area {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.35rem;
    min-height: 2.5rem;
    padding: 0.35rem 0.5rem;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-radius: var(--radius-xs, 4px);
    background: var(--color-surface-2, #11151c);
    box-shadow: inset 0 2px 8px rgba(0, 0, 0, 0.30);
    transition: border-color 0.18s, box-shadow 0.18s;
  }

  .picker-input-area.is-focused {
    border-color: var(--color-border-accent, rgba(216, 217, 220, 0.28));
    box-shadow: inset 0 2px 8px rgba(0, 0, 0, 0.30), var(--shadow-focus-accent);
  }

  .picker-input-area.is-disabled {
    opacity: 0.5;
    pointer-events: none;
  }

  .picker-input-area.is-error {
    border-color: rgba(239, 68, 68, 0.6);
  }

  .picker-search-inline {
    flex: 1;
    min-width: 6rem;
    background: transparent;
    border: none;
    color: var(--color-text-primary, #f5f2ea);
    font-size: 0.82rem;
    outline: none;
  }

  .picker-search-inline::placeholder {
    color: var(--color-text-disabled, #5a6070);
  }

  /* ── Chips ────────────────────────────────────────────── */

  .picker-chip {
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    max-width: 14rem;
    padding: 0.2rem 0.45rem;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-radius: var(--radius-xs, 4px);
    background: var(--color-surface-3, #181d27);
    font-size: 0.72rem;
    font-weight: 500;
    color: var(--color-text-secondary, #c8ccd4);
    transition: border-color 0.15s;
  }

  .picker-chip:hover {
    border-color: var(--color-border-accent, rgba(216, 217, 220, 0.28));
  }

  .picker-chip.single {
    padding: 0.25rem 0.55rem;
    font-size: 0.78rem;
  }

  .chip-avatar {
    width: 1.1rem;
    height: 1.1rem;
    object-fit: cover;
    flex-shrink: 0;
  }

  .chip-remove {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 0.875rem;
    height: 0.875rem;
    padding: 0;
    border: none;
    background: transparent;
    color: var(--color-text-disabled, #5a6070);
    cursor: pointer;
    transition: color 0.15s;
  }

  .chip-remove:hover {
    color: var(--color-text-primary, #f5f2ea);
  }

  /* ── Dropdown ─────────────────────────────────────────── */

  .picker-dropdown {
    position: absolute;
    left: 0;
    right: 0;
    top: 100%;
    z-index: 50;
    margin-top: 0.25rem;
    overflow: hidden;
  }

  .picker-empty {
    padding: 0.75rem 1rem;
    margin: 0;
    text-align: center;
    font-size: 0.78rem;
    color: var(--color-text-disabled, #5a6070);
  }

  .picker-list {
    max-height: 16rem;
    overflow-y: auto;
    scrollbar-width: thin;
  }

  /* ── Options ──────────────────────────────────────────── */

  .picker-option {
    display: flex;
    align-items: center;
    gap: 0.6rem;
    width: 100%;
    padding: 0.45rem 0.65rem;
    border: none;
    background: transparent;
    color: var(--color-text-secondary, #c8ccd4);
    text-align: left;
    cursor: pointer;
    transition: background 0.12s, color 0.12s;
  }

  .picker-option.is-active {
    background: var(--color-surface-3, #181d27);
    color: var(--color-text-primary, #f5f2ea);
  }

  .picker-option.is-selected {
    opacity: 0.4;
    pointer-events: none;
  }

  .picker-option.picker-add {
    border-top: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
  }

  .picker-option.picker-add.is-active {
    background: var(--color-accent-overlay-faint);
  }

  /* ── Option avatar ────────────────────────────────────── */

  .option-avatar {
    flex-shrink: 0;
    width: 2rem;
    height: 2rem;
    overflow: hidden;
    background: var(--color-surface-3, #181d27);
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.06));
    border-radius: var(--radius-xs, 4px);
  }

  .option-avatar img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }

  .avatar-placeholder {
    display: grid;
    place-items: center;
    width: 100%;
    height: 100%;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.82rem;
    font-weight: 600;
    color: var(--color-text-muted, #a4acb9);
  }

  .add-avatar {
    border-color: var(--color-border-accent);
    color: var(--color-text-accent, #c79b5c);
    display: grid;
    place-items: center;
  }

  /* ── Option text ──────────────────────────────────────── */

  .option-text {
    display: flex;
    flex-direction: column;
    min-width: 0;
    gap: 0.05rem;
  }

  .option-title {
    font-size: 0.82rem;
    font-weight: 500;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .option-subtitle {
    font-size: 0.68rem;
    color: var(--color-text-disabled, #5a6070);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .add-title {
    color: var(--color-text-accent, #c79b5c);
  }
</style>
