<script lang="ts" module>
  /** Presentation-only choice. Callers retain search, creation, and persistence. */
  export interface ChoicePickerOption {
    value: string;
    label: string;
    description?: string;
    image?: string | null;
    count?: number;
    isNew?: boolean;
  }
</script>

<script lang="ts">
  import { tick } from "svelte";
  import { ChevronDown, Plus, X } from "@lucide/svelte";
  import * as Command from "../components/ui/command";
  import * as Popover from "../components/ui/popover";
  import Button, { buttonVariants } from "./Button.svelte";
  import Badge from "./Badge.svelte";
  import { cn } from "../lib/utils";

  interface Props {
    id?: string;
    label: string;
    placeholder?: string;
    options: ChoicePickerOption[];
    selected: ChoicePickerOption[];
    multiple?: boolean;
    showSelected?: boolean;
    allowClear?: boolean;
    disabled?: boolean;
    invalid?: boolean;
    describedBy?: string;
    open?: boolean;
    query?: string;
    loading?: boolean;
    error?: string | null;
    emptyText?: string;
    createLabel?: string;
    accentChips?: boolean;
    onSelect: (value: string) => void;
    onRemove: (value: string) => void;
    onCreate?: () => void;
    onRetry?: () => void;
  }
  let { id, label, placeholder = "Select…", options, selected, multiple = false,
    showSelected = true, allowClear = true, disabled = false, invalid = false, describedBy,
    open = $bindable(false), query = $bindable(""), loading = false, error = null,
    emptyText = "No matches", createLabel, accentChips = false, onSelect, onRemove, onCreate, onRetry }: Props = $props();
  let trigger = $state<HTMLButtonElement | null>(null);
  let input = $state<HTMLInputElement | null>(null);
  let command = $state<Command.CommandRootApi | null>(null);

  function select(value: string) {
    onSelect(value);
    query = "";
    if (!multiple) open = false;
  }

  // Async callers replace their results after the command has mounted.
  $effect(() => {
    options; query;
    void tick().then(() => command?.updateSelectedToIndex(0));
  });
</script>

<div class="flex min-w-0 flex-col gap-2">
  {#if multiple && showSelected && selected.length}
    <div class="flex flex-wrap gap-1.5" aria-label={`Selected ${label}`}>
      {#each selected as option (option.value)}
        <Badge variant={option.isNew || accentChips ? "accent" : "default"} class="max-w-full gap-1 pr-1">
          {#if option.image}<img src={option.image} alt="" class="size-5 shrink-0 rounded-xs object-cover" />{/if}
          <span class="truncate">{option.label}</span>
          <Button variant="ghost" size="icon" class="size-6" {disabled}
            aria-label={`Remove ${option.label}`} onclick={() => onRemove(option.value)}><X /></Button>
        </Badge>
      {/each}
    </div>
  {/if}
  <div class="flex min-w-0 items-center gap-1">
    <Popover.Root bind:open onOpenChange={(next) => { if (next) query = ""; }}>
      <Popover.Trigger {id} bind:ref={trigger} {disabled} aria-invalid={invalid || undefined}
        aria-describedby={describedBy}
        aria-label={multiple ? `Add ${label}` : `${label}: ${selected[0]?.label ?? placeholder}`}
        class={cn(buttonVariants({ variant: "outline" }), "h-9 min-w-0 flex-1 justify-between px-3")}>
        <span class="truncate">{multiple || !showSelected ? placeholder : selected[0]?.label ?? placeholder}</span>
        <ChevronDown aria-hidden="true" />
      </Popover.Trigger>
      <Popover.Content align="start" aria-label={label} class="w-96 gap-0 p-1"
        portalProps={{ to: trigger?.closest("dialog") ?? undefined }}
        onOpenAutoFocus={(event) => { event.preventDefault(); input?.focus(); }}>
        <Command.Root bind:api={command} shouldFilter={false} label={`Search ${label}`}>
          <Command.Input bind:ref={input} bind:value={query} aria-label={`Search ${label}`} placeholder="Search…" />
          {#if loading}<p role="status" class="px-3 py-2 text-sm text-muted-foreground">Searching…</p>{/if}
          {#if error}
            <p role="alert" class="px-3 py-2 text-sm text-destructive">{error}</p>
            <Button variant="outline" size="sm" onclick={onRetry}
              onkeydown={(event) => event.stopPropagation()}>Retry</Button>
          {/if}
          <Command.List aria-label={label}>
            {#if !loading && !error && !options.length && !createLabel}
              <Command.Empty forceMount>{emptyText}</Command.Empty>
            {/if}
            <Command.Group>
              {#each options as option (option.value)}
                <Command.Item value={option.value} data-checked={selected.some((item) => item.value === option.value)}
                  onSelect={() => select(option.value)}>
                  {#if option.image}<img src={option.image} alt="" class="size-8 shrink-0 rounded-xs object-cover" />{/if}
                  <span class="flex min-w-0 flex-1 flex-col">
                    <span class="truncate">{option.label}</span>
                    {#if option.description}<span class="truncate text-xs text-muted-foreground">{option.description}</span>{/if}
                  </span>
                  {#if option.count != null}<span class="font-mono text-xs text-muted-foreground">{option.count}</span>{/if}
                </Command.Item>
              {/each}
              {#if createLabel && !loading && !error}
                <Command.Item value="__create__" showIndicator={false}
                  onSelect={() => { onCreate?.(); query = ""; if (!multiple) open = false; }}>
                  <Plus />{createLabel}
                </Command.Item>
              {/if}
            </Command.Group>
          </Command.List>
        </Command.Root>
      </Popover.Content>
    </Popover.Root>
    {#if !multiple && allowClear && selected.length}
      <Button variant="ghost" size="icon" {disabled} aria-label="Clear selection"
        onclick={() => onRemove(selected[0].value)}><X /></Button>
    {/if}
  </div>
</div>
