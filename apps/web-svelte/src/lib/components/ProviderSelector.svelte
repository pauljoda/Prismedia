<script module lang="ts">
  export interface ProviderGroup {
    label?: string;
    options: { value: string; label: string }[];
  }
</script>

<script lang="ts">
  import { onMount } from "svelte";
  import { ChevronDown, Search, Check } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";

  interface Props {
    value: string;
    onChange: (value: string) => void;
    groups: ProviderGroup[];
    allOption?: { value: string; label: string };
    disabled?: boolean;
    class?: string;
  }

  let { value, onChange, groups, allOption, disabled, class: className }: Props = $props();

  let open = $state(false);
  let search = $state("");
  let container: HTMLDivElement | null = $state(null);

  onMount(() => {
    function handleClickOutside(e: MouseEvent) {
      if (container && !container.contains(e.target as Node)) open = false;
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  });

  const selectedLabel = $derived.by(() => {
    if (allOption?.value === value) return allOption.label;
    for (const g of groups) {
      const opt = g.options.find((o) => o.value === value);
      if (opt) return opt.label;
    }
    return "";
  });

  const filteredGroups = $derived(
    groups
      .map((g) => ({
        ...g,
        options: g.options.filter((o) =>
          o.label.toLowerCase().includes(search.toLowerCase()),
        ),
      }))
      .filter((g) => g.options.length > 0),
  );

  const showAllOption = $derived(
    allOption &&
      (!search || allOption.label.toLowerCase().includes(search.toLowerCase())),
  );

  function pick(v: string) {
    onChange(v);
    open = false;
    search = "";
  }
</script>

<div bind:this={container} class={cn("relative", open && "z-50", className)}>
  <button
    type="button"
    {disabled}
    onclick={() => (open = !open)}
    class={cn(
      "control-input flex w-full items-center justify-between gap-2 text-left disabled:opacity-50 disabled:cursor-not-allowed",
      open && "border-border-accent ring-1 ring-border-accent",
    )}
  >
    <span class="truncate">{selectedLabel || "Select provider..."}</span>
    <ChevronDown class="h-3.5 w-3.5 opacity-50 shrink-0" />
  </button>

  {#if open}
    <div class="absolute z-50 mt-1 w-full min-w-[240px] rounded-sm border border-border-subtle bg-surface-2 shadow-xl">
      <div class="flex items-center gap-2 border-b border-border-subtle p-2">
        <Search class="h-3.5 w-3.5 text-text-disabled" />
        <!-- svelte-ignore a11y_autofocus -->
        <input
          type="text"
          autofocus
          placeholder="Search providers..."
          bind:value={search}
          class="w-full bg-transparent text-xs text-text-primary placeholder:text-text-disabled focus:outline-none"
        />
      </div>
      <div class="max-h-[50vh] overflow-y-auto p-1">
        {#if showAllOption && allOption}
          <button
            type="button"
            onclick={() => pick(allOption.value)}
            class={cn(
              "flex w-full items-center justify-between px-2 py-1.5 text-xs text-left hover:bg-surface-3 transition-colors",
              value === allOption.value && "bg-accent-950 text-text-accent",
            )}
          >
            <span class="truncate">{allOption.label}</span>
            {#if value === allOption.value}
              <Check class="h-3.5 w-3.5 shrink-0" />
            {/if}
          </button>
        {/if}

        {#if filteredGroups.length === 0 && !showAllOption}
          <div class="p-3 text-center text-xs text-text-disabled">No providers found.</div>
        {:else}
          {#each filteredGroups as group, idx (idx)}
            <div class="mb-1 last:mb-0">
              {#if group.label}
                <div class="px-2 py-1.5 text-[0.65rem] font-semibold uppercase tracking-wider text-text-disabled">
                  {group.label}
                </div>
              {/if}
              {#each group.options as opt (opt.value)}
                <button
                  type="button"
                  onclick={() => pick(opt.value)}
                  class={cn(
                    "flex w-full items-center justify-between px-2 py-1.5 text-xs text-left hover:bg-surface-3 transition-colors",
                    value === opt.value && "bg-accent-950 text-text-accent",
                  )}
                >
                  <span class="truncate">{opt.label}</span>
                  {#if value === opt.value}
                    <Check class="h-3.5 w-3.5 shrink-0" />
                  {/if}
                </button>
              {/each}
            </div>
          {/each}
        {/if}
      </div>
    </div>
  {/if}
</div>
