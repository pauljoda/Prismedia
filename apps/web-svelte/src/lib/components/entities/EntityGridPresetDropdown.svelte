<script lang="ts">
  import { Bookmark, Check, Plus, Trash2, X } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import { keepFlyoutOnScreen } from "$lib/actions/keep-flyout-on-screen";
  import type { FilterPreset } from "$lib/filter-presets";

  interface Props {
    activePresetId?: string | null;
    onApplyPreset?: (preset: FilterPreset) => void;
    onDeletePreset?: (id: string) => void;
    onOverwritePreset?: (id: string) => void;
    onSavePreset?: (name: string) => void;
    presets?: FilterPreset[];
  }

  let {
    activePresetId = null,
    onApplyPreset,
    onDeletePreset,
    onOverwritePreset,
    onSavePreset,
    presets = [],
  }: Props = $props();

  let nameInput: HTMLInputElement | undefined = $state();
  let open = $state(false);
  let saveName = $state("");
  let saving = $state<"idle" | "name" | "confirm">("idle");

  const activePreset = $derived(presets.find((preset) => preset.id === activePresetId));

  $effect(() => {
    if (saving === "name" && nameInput) nameInput.focus();
  });

  function close() {
    open = false;
    saving = "idle";
  }

  function handleConfirmSave() {
    const trimmed = saveName.trim();
    if (!trimmed) return;
    onSavePreset?.(trimmed);
    saveName = "";
    close();
  }

  function handleSaveClick() {
    if (activePresetId) {
      saving = "confirm";
      return;
    }

    saveName = "";
    saving = "name";
  }

  function handleOverwrite() {
    if (activePresetId) onOverwritePreset?.(activePresetId);
    close();
  }
</script>

<div class="relative">
  <button
    type="button"
    onclick={() => {
      open = !open;
      saving = "idle";
    }}
    class={cn(
      "preset-btn",
      activePresetId && "is-active",
    )}
    title={activePreset ? `Preset: ${activePreset.name}` : "Filter presets"}
  >
    <Bookmark class="h-3.5 w-3.5" />
    <span class="preset-label">{activePreset ? activePreset.name : "Presets"}</span>
  </button>

  {#if open}
    <button
      type="button"
      class="fixed inset-0 z-40"
      aria-label="Close preset menu"
      onclick={close}
    ></button>
    <div class="surface-glass absolute right-0 top-full z-50 mt-1.5 w-56 py-1" use:keepFlyoutOnScreen>
      {#if presets.length > 0}
        <div class="tag-scroll-area max-h-48 overflow-y-auto">
          {#each presets as preset (preset.id)}
            <div class={cn("preset-item group", preset.id === activePresetId && "is-active")}>
              <Check
                class={cn(
                  "h-3 w-3 shrink-0",
                  preset.id === activePresetId ? "opacity-100" : "opacity-0",
                )}
              />
              <button
                type="button"
                class="min-w-0 flex-1 truncate text-left"
                onclick={() => {
                  onApplyPreset?.(preset);
                  close();
                }}
              >
                {preset.name}
              </button>
              <button
                type="button"
                class="preset-delete"
                title="Delete preset"
                aria-label={`Delete preset ${preset.name}`}
                onclick={(event) => {
                  event.stopPropagation();
                  onDeletePreset?.(preset.id);
                }}
              >
                <Trash2 class="h-3 w-3" />
              </button>
            </div>
          {/each}
        </div>
      {:else}
        <div class="preset-empty">No saved presets</div>
      {/if}

      <div class="preset-divider"></div>

      {#if saving === "idle"}
        <button type="button" class="preset-action" onclick={handleSaveClick}>
          <Plus class="h-3 w-3" />
          Save current filters
        </button>
      {:else if saving === "name"}
        <div class="preset-form">
          <input
            bind:this={nameInput}
            bind:value={saveName}
            type="text"
            placeholder="Preset name..."
            class="preset-input"
            onkeydown={(event) => {
              if (event.key === "Enter") handleConfirmSave();
              if (event.key === "Escape") saving = "idle";
            }}
          />
          <div class="preset-form-row">
            <button
              type="button"
              class={cn("preset-form-btn preset-form-btn-primary", !saveName.trim() && "is-disabled")}
              disabled={!saveName.trim()}
              onclick={handleConfirmSave}
            >
              Save
            </button>
            <button
              type="button"
              class="preset-form-btn"
              aria-label="Cancel"
              onclick={() => (saving = "idle")}
            >
              <X class="h-3 w-3" />
            </button>
          </div>
        </div>
      {:else if saving === "confirm" && activePreset}
        <div class="preset-form">
          <div class="preset-confirm-label">
            Overwrite <span class="preset-confirm-name">{activePreset.name}</span>?
          </div>
          <div class="preset-form-row">
            <button type="button" class="preset-form-btn preset-form-btn-primary" onclick={handleOverwrite}>
              Overwrite
            </button>
            <button
              type="button"
              class="preset-form-btn"
              onclick={() => {
                saveName = "";
                saving = "name";
              }}
            >
              Save as new
            </button>
          </div>
          <button type="button" class="preset-cancel" onclick={() => (saving = "idle")}>
            Cancel
          </button>
        </div>
      {/if}
    </div>
  {/if}
</div>

<style>
  .preset-btn {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    height: 2rem;
    min-height: 2rem;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-2, #101420);
    border-radius: var(--radius-xs, 4px);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.7rem;
    letter-spacing: 0.04em;
    padding: 0 0.6rem;
    transition:
      background var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      border-color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      color var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1)),
      box-shadow var(--duration-fast, 80ms) var(--ease-default, cubic-bezier(0.4, 0, 0.2, 1));
  }

  .preset-btn:hover {
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-primary);
    box-shadow: 0 0 0 1px rgba(199, 201, 204,0.35), 0 0 8px rgba(199, 201, 204,0.15);
  }

  .preset-btn.is-active {
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    background: var(--color-surface-4, #1c2235);
    color: var(--color-text-accent, #c7c9cc);
    box-shadow: 0 0 0 1px rgba(199, 201, 204,0.35), 0 0 8px rgba(199, 201, 204,0.15);
  }

  .preset-label {
    display: none;
  }

  @media (min-width: 520px) {
    .preset-label {
      display: inline;
    }
  }

  .surface-glass {
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: rgba(12, 15, 21, 0.98);
    backdrop-filter: blur(var(--glass-blur-lg));
    -webkit-backdrop-filter: blur(var(--glass-blur-lg));
    border-radius: var(--radius-sm, 6px);
    box-shadow: 0 8px 40px rgba(0,0,0,0.60);
    overflow: hidden;
  }

  .preset-item {
    display: flex;
    align-items: center;
    gap: 0.35rem;
    width: calc(100% - 0.4rem);
    margin: 0 0.2rem;
    padding: 0.45rem 0.65rem;
    border-radius: var(--radius-xs, 4px);
    border: 1px solid transparent;
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
    letter-spacing: 0.04em;
    transition:
      background var(--duration-fast) var(--ease-default),
      border-color var(--duration-fast) var(--ease-default),
      color var(--duration-fast) var(--ease-default);
  }

  .preset-item:hover {
    background: rgb(255 255 255 / 0.04);
    border-color: var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    color: var(--color-text-primary);
  }

  .preset-item.is-active {
    background: linear-gradient(90deg, rgb(199 201 204 / 0.12), transparent);
    border-color: rgb(199 201 204 / 0.18);
    color: var(--color-text-accent, #c49a5a);
  }

  .preset-delete {
    flex-shrink: 0;
    color: var(--color-text-disabled);
    opacity: 0;
    transition: opacity var(--duration-fast) var(--ease-default), color var(--duration-fast) var(--ease-default);
  }

  .preset-item:hover .preset-delete,
  .group:hover .preset-delete {
    opacity: 1;
  }

  .preset-delete:hover {
    color: var(--color-error-text, #cc7880);
  }

  .preset-empty {
    padding: 0.6rem 0.85rem;
    text-align: center;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    color: var(--color-text-disabled);
  }

  .preset-divider {
    height: 1px;
    margin: 0.25rem 0.5rem;
    background: linear-gradient(
      to right,
      transparent,
      var(--color-border-subtle, rgba(148, 158, 178, 0.07)) 30%,
      var(--color-border-subtle, rgba(148, 158, 178, 0.07)) 70%,
      transparent
    );
  }

  .preset-action {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    width: calc(100% - 0.4rem);
    margin: 0 0.2rem;
    padding: 0.45rem 0.65rem;
    border-radius: var(--radius-xs, 4px);
    border: 1px solid transparent;
    background: transparent;
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
    letter-spacing: 0.04em;
    text-align: left;
    transition:
      background var(--duration-fast) var(--ease-default),
      border-color var(--duration-fast) var(--ease-default),
      color var(--duration-fast) var(--ease-default);
  }

  .preset-action:hover {
    background: rgb(255 255 255 / 0.04);
    border-color: var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    color: var(--color-text-primary);
  }

  .preset-form {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
    padding: 0.5rem 0.65rem;
  }

  .preset-input {
    width: 100%;
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    border-radius: var(--radius-xs, 4px);
    background: var(--color-surface-1, #0c0f15);
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30);
    padding: 0.35rem 0.5rem;
    color: var(--color-text-primary);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.7rem;
    outline: none;
    transition:
      border-color var(--duration-fast) var(--ease-default),
      box-shadow var(--duration-fast) var(--ease-default);
  }

  .preset-input::placeholder {
    color: var(--color-text-disabled);
  }

  .preset-input:focus {
    border-color: var(--color-border-accent, rgba(199, 201, 204, 0.25));
    box-shadow: inset 0 2px 8px rgba(0,0,0,0.30), 0 0 0 1px rgba(199, 201, 204,0.35), 0 0 8px rgba(199, 201, 204,0.15);
  }

  .preset-form-row {
    display: flex;
    gap: 0.25rem;
  }

  .preset-form-btn {
    flex: 1;
    padding: 0.35rem 0.5rem;
    border-radius: var(--radius-xs, 4px);
    border: 1px solid var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--color-surface-2, #101420);
    color: var(--color-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.65rem;
    font-weight: 600;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    transition:
      background var(--duration-fast) var(--ease-default),
      border-color var(--duration-fast) var(--ease-default),
      color var(--duration-fast) var(--ease-default),
      box-shadow var(--duration-fast) var(--ease-default);
  }

  .preset-form-btn:hover {
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-primary);
  }

  .preset-form-btn-primary {
    border-color: rgb(199 201 204 / 0.25);
    background: rgb(38 31 15 / 0.5);
    color: var(--color-accent-200, #ebdaaf);
  }

  .preset-form-btn-primary:hover {
    background: rgb(61 48 22 / 0.5);
    box-shadow: 0 0 8px rgb(199 201 204 / 0.15);
  }

  .preset-form-btn.is-disabled {
    cursor: not-allowed;
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-disabled);
    border-color: transparent;
  }

  .preset-confirm-label {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    color: var(--color-text-muted);
  }

  .preset-confirm-name {
    color: var(--color-text-accent, #c49a5a);
  }

  .preset-cancel {
    width: 100%;
    text-align: center;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.6rem;
    color: var(--color-text-disabled);
    background: transparent;
    border: none;
    transition: color var(--duration-fast) var(--ease-default);
  }

  .preset-cancel:hover {
    color: var(--color-text-muted);
  }
</style>
