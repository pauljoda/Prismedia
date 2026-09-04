<script lang="ts" module>
  /** A local choice for a searchable single-select; values must be unique. */
  export interface SearchableSelectOption {
    value: string;
    label: string;
    description?: string;
    disabled?: boolean;
  }
</script>

<script lang="ts">
  import ChevronDown from "@lucide/svelte/icons/chevron-down";
  import * as Command from "../components/ui/command";
  import * as Popover from "../components/ui/popover";
  import { buttonVariants } from "./Button.svelte";
  import { cn } from "../lib/utils";

  interface Props {
    options: SearchableSelectOption[];
    value?: string;
    label: string;
    searchLabel: string;
    placeholder?: string;
    emptyText?: string;
    maxResults?: number;
    disabled?: boolean;
    class?: string;
    onchange: (value: string) => void;
  }

  let { options, value, label, searchLabel, placeholder = "Select…", emptyText = "No matches found",
    maxResults = 50, disabled = false, class: className, onchange }: Props = $props();
  let open = $state(false);
  let query = $state("");
  let trigger = $state<HTMLButtonElement | null>(null);
  let searchInput = $state<HTMLInputElement | null>(null);
  const selected = $derived(options.find((option) => option.value === value));
  const normalizedQuery = $derived(query.trim().toLowerCase());
  const visibleOptions = $derived(options.filter((option) =>
    option.label.toLowerCase().includes(normalizedQuery) || option.value.toLowerCase().includes(normalizedQuery),
  ).slice(0, maxResults));

  function select(next: string) {
    open = false;
    onchange(next);
  }
</script>

<Popover.Root bind:open onOpenChange={(next) => { if (next) query = ""; }}>
  <Popover.Trigger bind:ref={trigger} disabled={disabled || options.length === 0}
    aria-label={`${label}: ${selected?.label ?? placeholder}`}
    class={cn(buttonVariants({ variant: "secondary" }), "h-9 w-full min-w-0 justify-between gap-3 px-3", className)}>
    <span class="flex min-w-0 items-baseline gap-2">
      <span class="shrink-0 text-xs text-text-muted">{label}</span>
      <span class="truncate text-sm text-text-primary">{selected?.label ?? placeholder}</span>
    </span>
    <ChevronDown class="size-4 shrink-0 text-text-muted" aria-hidden="true" />
  </Popover.Trigger>
  <Popover.Content align="start" aria-label={label} class="w-80 gap-0 p-1"
    onOpenAutoFocus={(event) => { event.preventDefault(); searchInput?.focus(); }}
    portalProps={{ to: trigger?.closest("dialog") ?? undefined }}>
    <Command.Root label={searchLabel} value={value ?? ""} shouldFilter={false}>
      <Command.Input bind:ref={searchInput} bind:value={query} placeholder={`${searchLabel}…`} aria-label={searchLabel} />
      <Command.List aria-label={label}>
        {#if visibleOptions.length === 0}
          <Command.Empty forceMount>{emptyText}</Command.Empty>
        {/if}
        <Command.Group>
          {#each visibleOptions as option (option.value)}
            <Command.Item value={option.value} disabled={option.disabled} data-checked={option.value === value}
              onSelect={() => select(option.value)}>
              <span class="flex min-w-0 flex-col gap-0.5">
                <span class="[overflow-wrap:anywhere]">{option.label}</span>
                {#if option.description}<span class="font-mono text-xs text-text-muted [overflow-wrap:anywhere]">{option.description}</span>{/if}
              </span>
            </Command.Item>
          {/each}
        </Command.Group>
      </Command.List>
    </Command.Root>
  </Popover.Content>
</Popover.Root>
