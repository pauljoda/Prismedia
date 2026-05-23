<script lang="ts">
  import { onMount } from "svelte";
  import { X } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";

  interface Props {
    values: string[];
    onChange: (values: string[]) => void;
    suggestions: { name: string; count?: number }[];
    placeholder?: string;
    newItems?: Set<string>;
  }

  let { values, onChange, suggestions, placeholder, newItems }: Props = $props();

  let input = $state("");
  let showDropdown = $state(false);
  let activeIndex = $state(-1);
  let inputEl: HTMLInputElement | null = $state(null);
  let container: HTMLDivElement | null = $state(null);

  const available = $derived(
    suggestions.filter(
      (s) => !values.some((v) => v.toLowerCase() === s.name.toLowerCase()),
    ),
  );
  const filtered = $derived(
    input.trim()
      ? available.filter((s) => s.name.toLowerCase().includes(input.toLowerCase()))
      : available,
  );
  const inputTrimmed = $derived(input.trim());
  const showAddOption = $derived(
    !!inputTrimmed &&
      !suggestions.some((s) => s.name.toLowerCase() === inputTrimmed.toLowerCase()) &&
      !values.some((v) => v.toLowerCase() === inputTrimmed.toLowerCase()),
  );
  const displayItems = $derived(filtered.slice(0, 12));
  const totalDropdownItems = $derived(displayItems.length + (showAddOption ? 1 : 0));

  function addValue(name: string) {
    const trimmed = name.trim();
    if (trimmed && !values.some((v) => v.toLowerCase() === trimmed.toLowerCase())) {
      onChange([...values, trimmed]);
    }
    input = "";
    showDropdown = false;
    activeIndex = -1;
    inputEl?.focus();
  }

  function removeValue(idx: number) {
    onChange(values.filter((_, i) => i !== idx));
  }

  function handleKeyDown(e: KeyboardEvent) {
    if (e.key === "Enter" || e.key === "Tab" || e.key === ",") {
      e.preventDefault();
      if (activeIndex >= 0 && activeIndex < displayItems.length) {
        addValue(displayItems[activeIndex].name);
      } else if (activeIndex === displayItems.length && showAddOption) {
        addValue(inputTrimmed);
      } else if (inputTrimmed) {
        addValue(inputTrimmed);
      }
    } else if (e.key === "Backspace" && !input && values.length > 0) {
      removeValue(values.length - 1);
    } else if (e.key === "ArrowDown") {
      e.preventDefault();
      activeIndex = Math.min(activeIndex + 1, totalDropdownItems - 1);
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      activeIndex = Math.max(activeIndex - 1, -1);
    } else if (e.key === "Escape") {
      showDropdown = false;
      activeIndex = -1;
    }
  }

  onMount(() => {
    function handleClick(e: MouseEvent) {
      if (container && !container.contains(e.target as Node)) showDropdown = false;
    }
    document.addEventListener("mousedown", handleClick);
    return () => document.removeEventListener("mousedown", handleClick);
  });

  function isNewItem(name: string): boolean {
    return newItems?.has(name.toLowerCase()) ?? false;
  }
</script>

<div bind:this={container} class="relative">
  <!-- svelte-ignore a11y_click_events_have_key_events -->
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="chip-input-container" onclick={() => inputEl?.focus()}>
    {#each values as v, i (v)}
      <span class={cn("chip-removable", isNewItem(v) && "chip-removable-new")}>
        {v}
        <button
          type="button"
          class="chip-remove-btn"
          onclick={(e) => {
            e.stopPropagation();
            removeValue(i);
          }}
          aria-label={`Remove ${v}`}
        >
          <X class="h-2.5 w-2.5" />
        </button>
      </span>
    {/each}
    <input
      bind:this={inputEl}
      bind:value={input}
      oninput={() => {
        showDropdown = true;
        activeIndex = -1;
      }}
      onfocus={() => (showDropdown = true)}
      onkeydown={handleKeyDown}
      placeholder={values.length === 0 ? placeholder ?? "" : ""}
    />
  </div>

  {#if showDropdown && totalDropdownItems > 0}
    <div class="autocomplete-dropdown">
      {#each displayItems as s, i (s.name)}
        <!-- svelte-ignore a11y_click_events_have_key_events -->
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <div
          class={cn("autocomplete-item", i === activeIndex && "autocomplete-item-active")}
          onmousedown={(e) => {
            e.preventDefault();
            addValue(s.name);
          }}
          onmouseenter={() => (activeIndex = i)}
        >
          {s.name}
          {#if s.count != null}
            <span class="autocomplete-item-count">{s.count}</span>
          {/if}
        </div>
      {/each}
      {#if showAddOption}
        <!-- svelte-ignore a11y_click_events_have_key_events -->
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <div
          class={cn(
            "autocomplete-item border-t border-border-subtle text-text-accent",
            activeIndex === displayItems.length && "autocomplete-item-active",
          )}
          onmousedown={(e) => {
            e.preventDefault();
            addValue(inputTrimmed);
          }}
          onmouseenter={() => (activeIndex = displayItems.length)}
        >
          + Add "{inputTrimmed}"
        </div>
      {/if}
    </div>
  {/if}
</div>
