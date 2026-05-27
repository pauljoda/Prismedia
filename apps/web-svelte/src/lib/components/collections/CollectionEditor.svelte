<script lang="ts">
  import { goto } from "$app/navigation";
  import { ArrowLeft, Eye, FolderPlus, Save, ShieldAlert, Timer, Type } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import type { CollectionDetail } from "$lib/api/generated/model";
  import { createCollection, previewCollectionRules, updateCollection } from "$lib/api/collections";
  import { getDescription, isNsfw as hasNsfw } from "$lib/api/capabilities";
  import {
    EMPTY_COLLECTION_RULE,
    type CollectionCoverMode,
    type CollectionMode,
    type CollectionRuleGroup,
    type CollectionWriteRequest,
  } from "$lib/collections/models";
  import TextAreaField from "$lib/components/forms/TextAreaField.svelte";
  import TextField from "$lib/components/forms/TextField.svelte";
  import ToggleChip from "$lib/components/forms/ToggleChip.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";
  import { durationToSeconds } from "$lib/utils/format";
  import ConditionBuilder from "./ConditionBuilder.svelte";

  interface Props {
    collection?: CollectionDetail | null;
    isNew?: boolean;
  }

  let { collection = null, isNew = false }: Props = $props();

  const appChrome = useAppChrome();
  const modes: { value: CollectionMode; label: string; meta: string }[] = [
    { value: "manual", label: "Manual", meta: "Pinned" },
    { value: "dynamic", label: "Dynamic", meta: "Rules" },
    { value: "hybrid", label: "Hybrid", meta: "Both" },
  ];

  let hydratedId = $state<string | null>(null);
  let title = $state("");
  let description = $state("");
  let mode = $state<CollectionMode>("manual");
  let coverMode = $state<CollectionCoverMode>("mosaic");
  let slideshowDurationSeconds = $state(5);
  let slideshowAutoAdvance = $state(true);
  let isNsfw = $state(false);
  let ruleTree = $state<CollectionRuleGroup>({ ...EMPTY_COLLECTION_RULE, children: [] });
  let saving = $state(false);
  let previewing = $state(false);
  let saveError = $state<string | null>(null);
  let previewError = $state<string | null>(null);
  let previewTotal = $state<number | null>(null);
  let previewByType = $state<Record<string, number>>({});

  const showRules = $derived(mode === "dynamic" || mode === "hybrid");
  const canSave = $derived(title.trim().length > 0 && !saving);

  $effect(() => {
    if (isNew) {
      return appChrome.setBreadcrumbs([
        { label: "Collections", href: "/collections" },
        { label: "New" },
      ]);
    }
    if (!collection) return;
    return appChrome.setBreadcrumbs([
      { label: "Collections", href: "/collections" },
      { label: collection.title, href: `/collections/${collection.id}` },
      { label: "Edit" },
    ]);
  });

  $effect(() => {
    const nextId = collection?.id ?? "__new__";
    if (hydratedId === nextId) return;
    hydratedId = nextId;

    if (!collection) {
      title = "";
      description = "";
      mode = "manual";
      coverMode = "mosaic";
      slideshowDurationSeconds = 5;
      slideshowAutoAdvance = true;
      isNsfw = false;
      ruleTree = { ...EMPTY_COLLECTION_RULE, children: [] };
      return;
    }

    title = collection.title;
    description = getDescription(collection.capabilities) ?? "";
    mode = normalizeMode(collection.mode);
    coverMode = normalizeCoverMode(collection.coverMode);
    slideshowDurationSeconds = durationToSeconds(collection.slideshowDuration) ?? 5;
    slideshowAutoAdvance = collection.slideshowAutoAdvance ?? true;
    isNsfw = hasNsfw(collection.capabilities);
    ruleTree = parseRuleTree(collection.ruleTreeJson);
  });

  function normalizeMode(value: string | null | undefined): CollectionMode {
    return value === "dynamic" || value === "hybrid" ? value : "manual";
  }

  function normalizeCoverMode(value: string | null | undefined): CollectionCoverMode {
    return value === "custom" || value === "item" ? value : "mosaic";
  }

  function parseRuleTree(raw: string | null | undefined): CollectionRuleGroup {
    if (!raw) return { ...EMPTY_COLLECTION_RULE, children: [] };
    try {
      const parsed = JSON.parse(raw);
      if (parsed?.type === "group" && Array.isArray(parsed.children)) {
        return parsed as CollectionRuleGroup;
      }
    } catch {
      return { ...EMPTY_COLLECTION_RULE, children: [] };
    }
    return { ...EMPTY_COLLECTION_RULE, children: [] };
  }

  function buildRequest(): CollectionWriteRequest {
    return {
      title: title.trim(),
      description: description.trim() ? description.trim() : null,
      mode,
      ruleTreeJson: showRules ? JSON.stringify(ruleTree) : null,
      coverMode,
      coverItemId: collection?.coverItemId ?? null,
      slideshowDurationSeconds: Math.max(1, Math.floor(slideshowDurationSeconds || 5)),
      slideshowAutoAdvance,
      isNsfw,
    };
  }

  async function save() {
    if (!canSave) return;
    saving = true;
    saveError = null;
    try {
      const saved = isNew || !collection
        ? await createCollection(buildRequest())
        : await updateCollection(collection.id, buildRequest());
      await goto(`/collections/${saved.id}`);
    } catch (err) {
      saveError = err instanceof Error ? err.message : "Failed to save collection.";
    } finally {
      saving = false;
    }
  }

  async function previewRules() {
    if (!showRules) return;
    previewing = true;
    previewError = null;
    try {
      const preview = await previewCollectionRules(JSON.stringify(ruleTree));
      previewTotal = preview.total;
      previewByType = Object.fromEntries(
        Object.entries(preview.byType).filter(([, value]) => typeof value === "number"),
      ) as Record<string, number>;
    } catch (err) {
      previewError = err instanceof Error ? err.message : "Failed to preview rules.";
    } finally {
      previewing = false;
    }
  }
</script>

<svelte:head>
  <title>{isNew ? "New Collection" : `Edit ${collection?.title ?? "Collection"}`} · Prismedia</title>
</svelte:head>

<section class="collection-editor">
  <header class="editor-head">
    <a href={collection ? `/collections/${collection.id}` : "/collections"} class="back-link">
      <ArrowLeft class="h-4 w-4" />
      Collections
    </a>
    <div>
      <p class="editor-kicker">Library · Collection</p>
      <h1>{isNew ? "New Collection" : "Edit Collection"}</h1>
    </div>
    <button type="button" class="save-button" disabled={!canSave} onclick={save}>
      <Save class="h-4 w-4" />
      {saving ? "Saving" : "Save"}
    </button>
  </header>

  {#if saveError}
    <div class="notice error">{saveError}</div>
  {/if}

  <div class="editor-grid">
    <section class="editor-main">
      <div class="form-band">
        <TextField
          value={title}
          onChange={(value) => (title = value)}
          label="Title"
          icon={Type}
          required
          disabled={saving}
        />
        <TextAreaField
          value={description}
          onChange={(value) => (description = value)}
          label="Description"
          rows={5}
          disabled={saving}
        />
      </div>

      <div class="form-band">
        <div class="mode-grid" aria-label="Collection mode">
          {#each modes as option (option.value)}
            <button
              type="button"
              class={cn("mode-option", mode === option.value && "active")}
              aria-pressed={mode === option.value}
              disabled={saving}
              onclick={() => (mode = option.value)}
            >
              <span>{option.label}</span>
              <small>{option.meta}</small>
            </button>
          {/each}
        </div>

        {#if showRules}
          <ConditionBuilder rule={ruleTree} onChange={(next) => (ruleTree = next)} disabled={saving} />
          <div class="preview-row">
            <button type="button" class="preview-button" disabled={previewing || saving} onclick={previewRules}>
              <Eye class="h-4 w-4" />
              {previewing ? "Previewing" : "Preview"}
            </button>
            {#if previewTotal !== null}
              <div class="preview-result">
                <span>{previewTotal} matches</span>
                {#each Object.entries(previewByType) as [kind, count] (kind)}
                  <small>{kind}: {count}</small>
                {/each}
              </div>
            {/if}
          </div>
          {#if previewError}
            <div class="notice error">{previewError}</div>
          {/if}
        {/if}
      </div>
    </section>

    <aside class="editor-side">
      <div class="side-panel">
        <h2><Timer class="h-4 w-4" /> Playback</h2>
        <TextField
          value={String(slideshowDurationSeconds)}
          onChange={(value) => (slideshowDurationSeconds = Number(value))}
          label="Image timer"
          type="number"
          min={1}
          max={3600}
          disabled={saving}
        />
        <ToggleChip
          value={slideshowAutoAdvance}
          onChange={(value) => (slideshowAutoAdvance = value)}
          onLabel="Auto advance"
          offLabel="Manual advance"
          disabled={saving}
        />
      </div>

      <div class="side-panel">
        <h2><FolderPlus class="h-4 w-4" /> Cover</h2>
        <label class="select-field">
          <span>Mode</span>
          <select bind:value={coverMode} disabled={saving}>
            <option value="mosaic">Mosaic</option>
            <option value="item">Item</option>
          </select>
        </label>
      </div>

      <div class="side-panel">
        <h2><ShieldAlert class="h-4 w-4" /> Visibility</h2>
        <ToggleChip
          value={isNsfw}
          onChange={(value) => (isNsfw = value)}
          onLabel="NSFW"
          offLabel="SFW"
          variant="warning"
          disabled={saving}
        />
      </div>
    </aside>
  </div>
</section>

<style>
  .collection-editor {
    display: grid;
    gap: 1rem;
    max-width: 84rem;
  }

  .editor-head {
    display: grid;
    grid-template-columns: auto 1fr auto;
    align-items: end;
    gap: 1rem;
    border-bottom: 1px solid var(--color-border-subtle);
    padding-bottom: 1rem;
  }

  .back-link {
    display: inline-flex;
    align-items: center;
    gap: 0.45rem;
    color: var(--color-text-muted);
    font-size: 0.78rem;
    text-decoration: none;
  }

  .back-link:hover {
    color: var(--color-text-primary);
  }

  .editor-kicker {
    margin: 0 0 0.25rem;
    font-family: var(--font-mono);
    font-size: 0.65rem;
    text-transform: uppercase;
    color: var(--color-text-disabled);
  }

  h1,
  h2 {
    margin: 0;
    font-family: var(--font-heading);
    color: var(--color-text-primary);
  }

  h1 {
    font-size: clamp(1.35rem, 2vw, 2rem);
  }

  h2 {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    font-size: 0.92rem;
  }

  .save-button,
  .preview-button {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.5rem;
    border: 1px solid rgba(242, 194, 106, 0.42);
    border-radius: var(--radius-xs);
    background: rgba(213, 154, 42, 0.16);
    color: var(--color-text-accent);
    padding: 0.62rem 0.85rem;
    font-size: 0.8rem;
    box-shadow: 0 0 18px rgb(242 194 106 / 0.1);
  }

  .save-button:disabled,
  .preview-button:disabled {
    cursor: not-allowed;
    opacity: 0.55;
  }

  .editor-grid {
    display: grid;
    grid-template-columns: minmax(0, 1fr) minmax(17rem, 22rem);
    gap: 1rem;
    align-items: start;
  }

  .editor-main,
  .editor-side {
    display: grid;
    gap: 1rem;
  }

  .form-band,
  .side-panel {
    display: grid;
    gap: 0.9rem;
    border: 1px solid var(--color-border-subtle);
    background: rgba(12, 15, 21, 0.72);
    padding: 1rem;
  }

  .mode-grid {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 0.55rem;
  }

  .mode-option {
    display: grid;
    gap: 0.2rem;
    min-height: 4.2rem;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-xs);
    background: var(--color-surface-2);
    color: var(--color-text-muted);
    padding: 0.8rem;
    text-align: left;
    transition: border-color 0.16s, color 0.16s, box-shadow 0.16s;
  }

  .mode-option span {
    font-family: var(--font-heading);
    font-size: 0.95rem;
    color: var(--color-text-primary);
  }

  .mode-option small {
    font-family: var(--font-mono);
    font-size: 0.65rem;
    color: var(--color-text-disabled);
  }

  .mode-option.active {
    border-color: rgba(242, 194, 106, 0.56);
    box-shadow: 0 0 18px rgb(242 194 106 / 0.13);
  }

  .preview-row {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.75rem;
  }

  .preview-result {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
    align-items: center;
    color: var(--color-text-muted);
    font-family: var(--font-mono);
    font-size: 0.7rem;
  }

  .preview-result span,
  .preview-result small {
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-xs);
    background: var(--color-surface-2);
    padding: 0.28rem 0.48rem;
  }

  .select-field {
    display: grid;
    gap: 0.4rem;
    color: var(--color-text-muted);
    font-size: 0.78rem;
  }

  .select-field span {
    font-family: var(--font-mono);
    font-size: 0.68rem;
    color: var(--color-text-disabled);
    text-transform: uppercase;
  }

  .select-field select {
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-xs);
    background: var(--color-surface-2);
    color: var(--color-text-primary);
    padding: 0.58rem 0.65rem;
  }

  .notice {
    border: 1px solid var(--color-border-subtle);
    background: var(--color-surface-2);
    padding: 0.75rem 0.9rem;
    color: var(--color-text-muted);
    font-size: 0.8rem;
  }

  .notice.error {
    border-color: color-mix(in srgb, var(--color-error) 55%, var(--color-border-subtle));
    color: var(--color-error-text);
  }

  @media (max-width: 900px) {
    .editor-head {
      grid-template-columns: 1fr;
      align-items: stretch;
    }

    .save-button {
      width: 100%;
    }

    .editor-grid {
      grid-template-columns: 1fr;
    }
  }

  @media (max-width: 640px) {
    .mode-grid {
      grid-template-columns: 1fr;
    }
  }
</style>
