<script lang="ts">
  import { onMount, tick, type Component } from "svelte";
  import { Plus, X } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import FormField from "./FormField.svelte";

  export interface TagOption {
    name: string;
    count?: number;
    isNew?: boolean;
    hint?: string;
  }

  interface Props {
    values: string[];
    onChange: (values: string[]) => void;
    options: TagOption[];
    label?: string;
    icon?: Component;
    placeholder?: string;
    helper?: string;
    error?: string;
    disabled?: boolean;
    canAddNew?: boolean;
    maxResults?: number;
    chipVariant?: "neutral" | "accent";
    newValues?: Set<string>;
  }

  let {
    values,
    onChange,
    options,
    label,
    icon,
    placeholder = "Add…",
    helper,
    error,
    disabled = false,
    canAddNew = true,
    maxResults = 12,
    chipVariant = "neutral",
    newValues,
  }: Props = $props();

  let query = $state("");
  let open = $state(false);
  let activeIndex = $state(0);
  let container: HTMLDivElement | null = $state(null);
  let inputEl: HTMLInputElement | null = $state(null);

  const trimmed = $derived(query.trim());
  const queryLower = $derived(trimmed.toLowerCase());
  const valuesLower = $derived(new Set(values.map((v) => v.toLowerCase())));

  const available = $derived(
    options.filter((o) => !valuesLower.has(o.name.toLowerCase())),
  );
  const filtered = $derived(
    queryLower
      ? available.filter((o) => o.name.toLowerCase().includes(queryLower))
      : available,
  );
  const limited = $derived(filtered.slice(0, maxResults));
  const hasExactMatch = $derived(
    !!trimmed && options.some((o) => o.name.toLowerCase() === queryLower),
  );
  const alreadySelected = $derived(!!trimmed && valuesLower.has(queryLower));
  const showAddOption = $derived(
    canAddNew && !!trimmed && !hasExactMatch && !alreadySelected,
  );
  const totalItems = $derived(limited.length + (showAddOption ? 1 : 0));

  const id = `tags-${Math.random().toString(36).slice(2, 9)}`;

  function isNewValue(name: string): boolean {
    return newValues?.has(name.toLowerCase()) ?? false;
  }

  function addValue(name: string) {
    const t = name.trim();
    if (!t) return;
    if (valuesLower.has(t.toLowerCase())) {
      query = "";
      return;
    }
    onChange([...values, t]);
    query = "";
    activeIndex = 0;
  }

  function removeAt(index: number) {
    onChange(values.filter((_, i) => i !== index));
  }

  async function focusInput() {
    await tick();
    inputEl?.focus();
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (e.key === "Enter" || e.key === "," || (e.key === "Tab" && trimmed)) {
      e.preventDefault();
      if (activeIndex < limited.length) {
        const opt = limited[activeIndex];
        if (opt) addValue(opt.name);
      } else if (showAddOption) {
        addValue(trimmed);
      } else if (trimmed) {
        addValue(trimmed);
      }
      return;
    }
    if (e.key === "Backspace" && query === "" && values.length > 0) {
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
    return () => document.removeEventListener("mousedown", handleClick);
  });
</script>

<FormField {label} {icon} {helper} {error} htmlFor={id}>
  <div bind:this={container} class="relative">
    <label
      for={id}
      class={cn(
        "flex min-h-[2.5rem] w-full flex-wrap items-center gap-1.5 border border-border-subtle bg-surface-2 px-2 py-1.5 transition-colors",
        "focus-within:border-border-accent focus-within:shadow-[var(--shadow-focus-accent)]",
        disabled && "opacity-50 pointer-events-none",
        error && "border-error/60",
      )}
      onfocusin={() => {
        open = true;
      }}
    >
      {#each values as value, i (value)}
        {@const isNew = isNewValue(value)}
        <span
          class={cn(
            "inline-flex items-center gap-1 px-2 py-0.5 text-[0.72rem] font-medium",
            chipVariant === "accent" || isNew
              ? "border border-accent-700/40 bg-accent-950/60 text-accent-200"
              : "border border-border-subtle bg-surface-3 text-text-secondary",
          )}
        >
          <span class="truncate">{value}</span>
          <button
            type="button"
            onclick={(e) => {
              e.stopPropagation();
              removeAt(i);
            }}
            aria-label={`Remove ${value}`}
            class="inline-flex h-3.5 w-3.5 items-center justify-center text-text-disabled transition-colors hover:text-text-primary"
          >
            <X class="h-2.5 w-2.5" />
          </button>
        </span>
      {/each}
      <input
        {id}
        bind:this={inputEl}
        bind:value={query}
        oninput={() => {
          open = true;
          activeIndex = 0;
        }}
        onfocus={() => (open = true)}
        onkeydown={handleKeyDown}
        placeholder={values.length === 0 ? placeholder : ""}
        class="min-w-[6rem] flex-1 bg-transparent text-sm text-text-primary placeholder:text-text-disabled focus:outline-none"
        aria-autocomplete="list"
        aria-expanded={open}
      />
    </label>

    {#if open && totalItems > 0}
      <div class="absolute left-0 right-0 top-full z-50 mt-1 surface-elevated overflow-hidden">
        <div role="listbox" class="max-h-64 overflow-y-auto py-1">
          {#each limited as option, i (option.name)}
            {@const active = i === activeIndex}
            <button
              type="button"
              role="option"
              aria-selected={active}
              onmouseenter={() => (activeIndex = i)}
              onmousedown={(e) => {
                e.preventDefault();
                addValue(option.name);
              }}
              class={cn(
                "flex w-full items-center justify-between gap-2 px-3 py-1.5 text-left text-sm transition-colors",
                active ? "bg-surface-3 text-text-primary" : "text-text-secondary",
              )}
            >
              <span class="flex min-w-0 flex-1 items-center gap-2 truncate">
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
                addValue(trimmed);
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
