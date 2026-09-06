<script lang="ts">
  import { Bookmark, Check, Plus, Trash2, X } from "@lucide/svelte";
  import { Button, buttonVariants, cn, Popover, Separator, TextInput } from "@prismedia/ui-svelte";
  import type { FilterPreset } from "$lib/filter-presets";

  interface Props {
    activePresetId?: string | null;
    onApplyPreset?: (preset: FilterPreset) => void;
    onDeletePreset?: (id: string) => void;
    onOverwritePreset?: (id: string) => void;
    onSavePreset?: (name: string) => void;
    presets?: FilterPreset[];
  }

  let { activePresetId = null, onApplyPreset, onDeletePreset, onOverwritePreset, onSavePreset, presets = [] }: Props = $props();

  const id = $props.id();
  let nameInput = $state<HTMLInputElement | null>(null);
  let open = $state(false);
  let saveName = $state("");
  let saving = $state<"idle" | "name" | "confirm">("idle");
  const activePreset = $derived(presets.find((preset) => preset.id === activePresetId));

  $effect(() => {
    if (saving === "name" && nameInput) nameInput.focus();
  });

  function resetForm() {
    saving = "idle";
    saveName = "";
  }

  function close() {
    open = false;
  }

  function save(event: SubmitEvent) {
    event.preventDefault();
    const trimmed = saveName.trim();
    if (!trimmed) return;
    onSavePreset?.(trimmed);
    close();
  }
</script>

<Popover.Root bind:open onOpenChange={(next) => { if (next) resetForm(); }}>
  <Popover.Trigger
    class={buttonVariants({ variant: "secondary", size: "md" })}
    title={activePreset ? `Preset: ${activePreset.name}` : "Filter presets"}
    aria-label={activePreset?.name ?? "Presets"}
  >
    <Bookmark class="size-3.5" />
    <span class="hidden max-w-36 truncate min-[520px]:inline">{activePreset?.name ?? "Presets"}</span>
  </Popover.Trigger>
  <Popover.Content align="end" aria-labelledby={`${id}-title`} aria-describedby={`${id}-description`}>
    <Popover.Header>
      <div class="flex items-center justify-between gap-2">
        <Popover.Title id={`${id}-title`}>Filter presets</Popover.Title>
        <Popover.Close class={buttonVariants({ variant: "ghost", size: "icon" })} aria-label="Close presets"><X class="size-4" /></Popover.Close>
      </div>
      <Popover.Description id={`${id}-description`}>Save a view to return to it later.</Popover.Description>
    </Popover.Header>

    {#if presets.length > 0}
      <div class="flex max-h-56 flex-col gap-1 overflow-y-auto" role="group" aria-label="Saved presets">
        {#each presets as preset (preset.id)}
          <div class="flex items-center gap-1">
            <Button
              variant={preset.id === activePresetId ? "secondary" : "ghost"}
              class="min-w-0 flex-1 justify-start"
              aria-label={`Apply preset ${preset.name}`}
              aria-pressed={preset.id === activePresetId}
              onclick={() => { onApplyPreset?.(preset); close(); }}
            >
              <Check class={cn("size-3.5 shrink-0", preset.id !== activePresetId && "invisible")} />
              <span class="truncate">{preset.name}</span>
            </Button>
            <Button variant="ghost" size="icon" aria-label={`Delete preset ${preset.name}`} onclick={() => onDeletePreset?.(preset.id)}>
              <Trash2 class="size-3.5" />
            </Button>
          </div>
        {/each}
      </div>
    {:else}
      <p class="py-2 text-sm text-text-muted">No saved presets</p>
    {/if}

    <Separator />

    {#if saving === "idle"}
      <Button variant="secondary" class="w-full" onclick={() => (saving = activePreset ? "confirm" : "name")}>
        <Plus class="size-4" />Save current filters
      </Button>
    {:else if saving === "name"}
      <form class="flex flex-col gap-3" onsubmit={save}>
        <label for={`${id}-name`} class="text-sm text-text-secondary">Preset name</label>
        <TextInput id={`${id}-name`} bind:ref={nameInput} value={saveName} maxlength={100} placeholder="e.g. Unread books" oninput={(event) => (saveName = event.currentTarget.value)} />
        <div class="flex justify-end gap-2">
          <Button variant="ghost" onclick={resetForm}>Cancel</Button>
          <Button type="submit" disabled={!saveName.trim()}>Save</Button>
        </div>
      </form>
    {:else if saving === "confirm" && activePreset}
      <div class="flex flex-col gap-3">
        <p class="text-sm text-text-secondary">Replace the saved filters in <strong class="font-medium text-text-primary">{activePreset.name}</strong>?</p>
        <div class="flex flex-wrap justify-end gap-2">
          <Button variant="ghost" onclick={resetForm}>Cancel</Button>
          <Button variant="secondary" onclick={() => (saving = "name")}>Save as new</Button>
          <Button onclick={() => { onOverwritePreset?.(activePreset.id); close(); }}>Overwrite</Button>
        </div>
      </div>
    {/if}
  </Popover.Content>
</Popover.Root>
