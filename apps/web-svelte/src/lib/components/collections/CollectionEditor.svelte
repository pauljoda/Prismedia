<script lang="ts">
  import { goto } from "$app/navigation";
  import {
    ArrowLeft,
    Eye,
    FolderPlus,
    Layers,
    Loader2,
    Save,
    ShieldAlert,
    Timer,
    Type,
    XCircle,
  } from "@lucide/svelte";
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
  const modes: { value: CollectionMode; label: string; desc: string; icon: typeof Layers }[] = [
    { value: "manual", label: "Manual", desc: "Hand-pick and order items", icon: Layers },
    { value: "dynamic", label: "Dynamic", desc: "Auto-populate from rules", icon: Layers },
    { value: "hybrid", label: "Hybrid", desc: "Rules plus manual pins", icon: Layers },
  ];

  const coverModes: { value: CollectionCoverMode; label: string }[] = [
    { value: "mosaic", label: "Mosaic" },
    { value: "item", label: "Single Item" },
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

<section class="grid gap-5 max-w-[84rem]">
  <!-- Header -->
  <header class="flex items-end justify-between gap-4 border-b border-border-subtle pb-4">
    <div class="flex items-end gap-4">
      <a
        href={collection ? `/collections/${collection.id}` : "/collections"}
        class="inline-flex items-center gap-1.5 text-text-muted text-[0.78rem] no-underline transition-colors hover:text-text-primary"
      >
        <ArrowLeft class="h-4 w-4" />
        <span class="hidden sm:inline">Collections</span>
      </a>
      <div>
        <p class="text-kicker mb-1">Library · Collection</p>
        <h1 class="m-0 font-heading text-text-primary text-[clamp(1.35rem,2vw,2rem)]">
          {isNew ? "New Collection" : "Edit Collection"}
        </h1>
      </div>
    </div>
    <div class="flex items-center gap-2">
      <a
        href={collection ? `/collections/${collection.id}` : "/collections"}
        class={cn(
          "inline-flex items-center gap-1.5 border border-border-subtle bg-surface-2 px-3 py-2 text-[0.78rem] text-text-muted no-underline transition-colors",
          "hover:border-border-default hover:text-text-primary",
        )}
      >
        <XCircle class="h-3.5 w-3.5" />
        Cancel
      </a>
      <button
        type="button"
        disabled={!canSave}
        onclick={save}
        class={cn(
          "inline-flex items-center gap-1.5 border border-border-accent bg-gradient-to-r from-accent-900 via-accent-800 to-accent-900 px-4 py-2 text-[0.78rem] font-medium text-accent-100 shadow-[var(--shadow-glow-accent)] transition-all",
          "hover:shadow-[var(--shadow-glow-accent-strong)]",
          "disabled:cursor-not-allowed disabled:opacity-50 disabled:shadow-none",
        )}
      >
        {#if saving}
          <Loader2 class="h-3.5 w-3.5 animate-spin" />
        {:else}
          <Save class="h-3.5 w-3.5" />
        {/if}
        {saving ? "Saving…" : "Save"}
      </button>
    </div>
  </header>

  {#if saveError}
    <div class="flex items-center gap-3 border border-error/50 bg-surface-2 px-4 py-3 text-[0.8rem] text-error-text">
      <ShieldAlert class="h-4 w-4 flex-shrink-0" />
      {saveError}
    </div>
  {/if}

  <!-- Main grid -->
  <div class="grid grid-cols-1 lg:grid-cols-[minmax(0,1fr)_minmax(17rem,22rem)] gap-5 items-start">
    <!-- Left: core fields -->
    <div class="grid gap-5">
      <!-- Title / Description -->
      <div class="surface-panel p-5 space-y-4">
        <TextField
          value={title}
          onChange={(value) => (title = value)}
          label="Title"
          icon={Type}
          placeholder="Collection name"
          required
          disabled={saving}
        />
        <TextAreaField
          value={description}
          onChange={(value) => (description = value)}
          label="Description"
          placeholder="What this collection is about…"
          rows={4}
          minHeightRem={4}
          disabled={saving}
        />
      </div>

      <!-- Mode selector -->
      <div class="surface-panel p-5 space-y-4">
        <p class="text-kicker">Collection Mode</p>
        <div class="grid grid-cols-3 gap-2 max-sm:grid-cols-1" role="radiogroup" aria-label="Collection mode">
          {#each modes as option (option.value)}
            {@const active = mode === option.value}
            <button
              type="button"
              role="radio"
              aria-checked={active}
              disabled={saving}
              onclick={() => (mode = option.value)}
              class={cn(
                "group grid gap-1 p-3 text-left border transition-all duration-normal",
                "disabled:cursor-not-allowed disabled:opacity-50",
                active
                  ? "border-border-accent-strong bg-accent-950/30 shadow-[var(--shadow-glow-accent)]"
                  : "border-border-subtle bg-surface-2 hover:border-border-default",
              )}
            >
              <span class={cn(
                "font-heading text-[0.9rem] font-semibold transition-colors",
                active ? "text-text-accent" : "text-text-primary",
              )}>
                {option.label}
              </span>
              <span class="text-[0.7rem] text-text-disabled leading-snug">
                {option.desc}
              </span>
            </button>
          {/each}
        </div>

        {#if showRules}
          <ConditionBuilder rule={ruleTree} onChange={(next) => (ruleTree = next)} disabled={saving} />

          <div class="flex flex-wrap items-center gap-3">
            <button
              type="button"
              disabled={previewing || saving}
              onclick={previewRules}
              class={cn(
                "inline-flex items-center gap-1.5 border border-border-subtle bg-surface-2 px-3 py-2 text-[0.78rem] text-text-muted transition-colors",
                "hover:border-border-accent hover:text-text-accent",
                "disabled:cursor-not-allowed disabled:opacity-50",
              )}
            >
              {#if previewing}
                <Loader2 class="h-3.5 w-3.5 animate-spin" />
              {:else}
                <Eye class="h-3.5 w-3.5" />
              {/if}
              {previewing ? "Previewing…" : "Preview Rules"}
            </button>
            {#if previewTotal !== null}
              <div class="flex flex-wrap items-center gap-1.5">
                <span class="inline-flex items-center gap-1 border border-border-accent bg-accent-950/40 px-2 py-1 text-[0.72rem] font-mono font-semibold text-text-accent tabular-nums">
                  {previewTotal} matches
                </span>
                {#each Object.entries(previewByType) as [kind, count] (kind)}
                  <span class="border border-border-subtle bg-surface-3 px-2 py-1 text-[0.68rem] font-mono text-text-muted tabular-nums">
                    {kind}: {count}
                  </span>
                {/each}
              </div>
            {/if}
          </div>
          {#if previewError}
            <p class="text-[0.72rem] text-error-text">{previewError}</p>
          {/if}
        {/if}
      </div>
    </div>

    <!-- Right: sidebar panels -->
    <div class="grid gap-5">
      <!-- Playback -->
      <div class="surface-panel p-5 space-y-4">
        <h2 class="text-kicker flex items-center gap-1.5">
          <Timer class="h-3 w-3" /> Playback
        </h2>
        <TextField
          value={String(slideshowDurationSeconds)}
          onChange={(value) => (slideshowDurationSeconds = Number(value))}
          label="Image timer (seconds)"
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

      <!-- Cover -->
      <div class="surface-panel p-5 space-y-4">
        <h2 class="text-kicker flex items-center gap-1.5">
          <FolderPlus class="h-3 w-3" /> Cover
        </h2>
        <div class="grid grid-cols-2 gap-2" role="radiogroup" aria-label="Cover mode">
          {#each coverModes as cm (cm.value)}
            {@const active = coverMode === cm.value}
            <button
              type="button"
              role="radio"
              aria-checked={active}
              disabled={saving}
              onclick={() => (coverMode = cm.value)}
              class={cn(
                "px-3 py-2 text-[0.78rem] border text-center transition-all duration-normal",
                "disabled:cursor-not-allowed disabled:opacity-50",
                active
                  ? "border-border-accent-strong bg-accent-950/30 text-text-accent shadow-[0_0_12px_rgba(242,194,106,0.12)]"
                  : "border-border-subtle bg-surface-2 text-text-muted hover:border-border-default hover:text-text-primary",
              )}
            >
              {cm.label}
            </button>
          {/each}
        </div>
      </div>

      <!-- Visibility -->
      <div class="surface-panel p-5 space-y-4">
        <h2 class="text-kicker flex items-center gap-1.5">
          <ShieldAlert class="h-3 w-3" /> Visibility
        </h2>
        <ToggleChip
          value={isNsfw}
          onChange={(value) => (isNsfw = value)}
          onLabel="NSFW"
          offLabel="SFW"
          variant="warning"
          disabled={saving}
        />
      </div>
    </div>
  </div>
</section>
