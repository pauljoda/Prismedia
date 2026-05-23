<script lang="ts">
  import { onMount, tick, type Component } from "svelte";
  import { Check, ChevronDown, Plus, Search, X } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import FormField from "./FormField.svelte";

  export interface SearchOption {
    id?: string;
    name: string;
    count?: number;
    hint?: string;
  }

  interface Props {
    value: string;
    onChange: (value: string) => void;
    options: SearchOption[];
    label?: string;
    icon?: Component;
    placeholder?: string;
    helper?: string;
    error?: string;
    required?: boolean;
    disabled?: boolean;
    canAddNew?: boolean;
    allowClear?: boolean;
    maxResults?: number;
    emptyText?: string;
  }

  let {
    value,
    onChange,
    options,
    label,
    icon,
    placeholder = "Select…",
    helper,
    error,
    required = false,
    disabled = false,
    canAddNew = false,
    allowClear = true,
    maxResults = 50,
    emptyText = "No matches",
  }: Props = $props();

  let open = $state(false);
  let query = $state("");
  let activeIndex = $state(0);
  let container: HTMLDivElement | null = $state(null);
  let searchInput: HTMLInputElement | null = $state(null);

  const trimmed = $derived(query.trim());
  const queryLower = $derived(trimmed.toLowerCase());

  const filtered = $derived(
    queryLower
      ? options.filter((o) => o.name.toLowerCase().includes(queryLower))
      : options,
  );
  const limited = $derived(filtered.slice(0, maxResults));
  const hasExact = $derived(
    !!trimmed && options.some((o) => o.name.toLowerCase() === queryLower),
  );
  const showAddOption = $derived(canAddNew && !!trimmed && !hasExact);
  const totalItems = $derived(limited.length + (showAddOption ? 1 : 0));
  const id = `select-${Math.random().toString(36).slice(2, 9)}`;

  async function openPanel() {
    if (disabled) return;
    open = true;
    query = "";
    activeIndex = 0;
    await tick();
    searchInput?.focus();
  }

  function closePanel() {
    open = false;
    query = "";
    activeIndex = 0;
  }

  function commit(name: string) {
    onChange(name);
    closePanel();
  }

  function clear(event?: MouseEvent) {
    event?.stopPropagation();
    onChange("");
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (e.key === "Escape") {
      e.preventDefault();
      closePanel();
      return;
    }
    if (e.key === "ArrowDown") {
      e.preventDefault();
      activeIndex = Math.min(activeIndex + 1, Math.max(0, totalItems - 1));
      return;
    }
    if (e.key === "ArrowUp") {
      e.preventDefault();
      activeIndex = Math.max(activeIndex - 1, 0);
      return;
    }
    if (e.key === "Enter") {
      e.preventDefault();
      if (activeIndex < limited.length) {
        const opt = limited[activeIndex];
        if (opt) commit(opt.name);
      } else if (showAddOption && trimmed) {
        commit(trimmed);
      }
    }
  }

  onMount(() => {
    function handleClick(e: MouseEvent) {
      if (open && container && !container.contains(e.target as Node)) {
        closePanel();
      }
    }
    document.addEventListener("mousedown", handleClick);
    return () => document.removeEventListener("mousedown", handleClick);
  });
</script>

<FormField {label} {icon} {helper} {error} {required} htmlFor={id}>
  <div bind:this={container} class="relative">
    <button
      {id}
      type="button"
      {disabled}
      onclick={() => (open ? closePanel() : openPanel())}
      aria-haspopup="listbox"
      aria-expanded={open}
      class={cn(
        "group/sel flex w-full items-center justify-between gap-2 border border-border-subtle bg-surface-2 px-3 py-2 text-left text-sm transition-colors",
        "focus:border-border-accent focus:outline-none focus:shadow-[var(--shadow-focus-accent)]",
        "disabled:cursor-not-allowed disabled:opacity-50",
        open && "border-border-accent shadow-[var(--shadow-focus-accent)]",
        error && "border-error/60",
      )}
    >
      <span class={cn("min-w-0 flex-1 truncate", value ? "text-text-primary" : "text-text-disabled")}>
        {value || placeholder}
      </span>
      {#if value && allowClear && !disabled}
        <span
          role="button"
          tabindex="-1"
          aria-label="Clear selection"
          onclick={clear}
          onkeydown={(e) => {
            if (e.key === "Enter" || e.key === " ") {
              e.preventDefault();
              clear();
            }
          }}
          class="inline-flex h-5 w-5 items-center justify-center text-text-disabled transition-colors hover:text-text-primary"
        >
          <X class="h-3 w-3" />
        </span>
      {/if}
      <ChevronDown class={cn("h-3.5 w-3.5 flex-shrink-0 text-text-muted transition-transform", open && "rotate-180")} />
    </button>

    {#if open}
      <div class="absolute left-0 right-0 top-full z-50 mt-1 surface-elevated overflow-hidden">
        <div class="flex items-center gap-2 border-b border-border-subtle bg-surface-1 px-3 py-2">
          <Search class="h-3.5 w-3.5 flex-shrink-0 text-text-disabled" />
          <input
            bind:this={searchInput}
            bind:value={query}
            oninput={() => (activeIndex = 0)}
            onkeydown={handleKeyDown}
            placeholder="Search…"
            class="w-full bg-transparent text-sm text-text-primary placeholder:text-text-disabled focus:outline-none"
            aria-label="Search options"
          />
        </div>
        <div role="listbox" class="max-h-64 overflow-y-auto py-1">
          {#if limited.length === 0 && !showAddOption}
            <p class="px-3 py-3 text-center text-[0.78rem] text-text-disabled">{emptyText}</p>
          {/if}
          {#each limited as option, i (option.id ?? option.name)}
            {@const active = i === activeIndex}
            {@const selected = option.name === value}
            <button
              type="button"
              role="option"
              aria-selected={selected}
              onmouseenter={() => (activeIndex = i)}
              onmousedown={(e) => {
                e.preventDefault();
                commit(option.name);
              }}
              class={cn(
                "flex w-full items-center justify-between gap-2 px-3 py-1.5 text-left text-sm transition-colors",
                active ? "bg-surface-3 text-text-primary" : "text-text-secondary",
                selected && "text-text-accent",
              )}
            >
              <span class="flex min-w-0 flex-1 items-center gap-2">
                {#if selected}<Check class="h-3 w-3 flex-shrink-0 text-accent-400" />{/if}
                <span class="truncate">{option.name}</span>
                {#if option.hint}
                  <span class="truncate text-[0.7rem] text-text-disabled">— {option.hint}</span>
                {/if}
              </span>
              {#if option.count != null}
                <span class="font-mono text-[0.68rem] tabular-nums text-text-disabled">{option.count}</span>
              {/if}
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
                commit(trimmed);
              }}
              class={cn(
                "flex w-full items-center gap-2 border-t border-border-subtle px-3 py-2 text-left text-sm transition-colors",
                addActive ? "bg-accent-950 text-accent-200" : "text-text-accent",
              )}
            >
              <Plus class="h-3.5 w-3.5" />
              <span class="truncate">Add "{trimmed}"</span>
            </button>
          {/if}
        </div>
      </div>
    {/if}
  </div>
</FormField>
