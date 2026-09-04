<script lang="ts">
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { onMount } from "svelte";
  import {
    Eye,
    FolderPlus,
    Layers,
    List,
    Loader2,
    Save,
    Share2,
    ShieldAlert,
    SlidersHorizontal,
    Type,
    XCircle,
    Zap,
  } from "@lucide/svelte";
  import type { Component } from "svelte";
  import { Button, RadioGroup, ToggleGroup, buttonVariants,  cn, Toggle  } from "@prismedia/ui-svelte";
  import type { EntityCard } from "$lib/api/generated/model";
  import {
    COLLECTION_COVER_MODE,
    COLLECTION_MODE,
    type CollectionCoverModeCode,
    type CollectionModeCode,
  } from "$lib/api/generated/codes";
  import { createCollection, previewCollectionRules, updateCollection } from "$lib/api/collections";
  import {
    getCollectionConfigurationCapability,
    getCoverSelectionCapability,
    getDescription,
    isNsfw as hasNsfw,
  } from "$lib/api/capabilities";
  import { fetchLibraryRoots, type LibraryRoot } from "$lib/api/settings";
  import {
    EMPTY_COLLECTION_RULE,
    type CollectionRuleGroup,
    type CollectionWriteRequest,
  } from "$lib/collections/models";
  import { rulesReadyForPreview } from "$lib/collections/rule-editor";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import TextAreaField from "$lib/components/forms/TextAreaField.svelte";
  import TextField from "$lib/components/forms/TextField.svelte";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";
  import ConditionBuilder from "./ConditionBuilder.svelte";

  interface Props {
    collection?: EntityCard | null;
    isNew?: boolean;
  }

  let { collection = null, isNew = false }: Props = $props();

  const appChrome = useAppChrome();
  const nsfw = useNsfw();
  const modes: { value: CollectionModeCode; label: string; desc: string; icon: Component }[] = [
    { value: COLLECTION_MODE.manual, label: "Manual", desc: "Hand-pick and order items", icon: List },
    { value: COLLECTION_MODE.dynamic, label: "Dynamic", desc: "Auto-populate from rules", icon: Zap },
    { value: COLLECTION_MODE.hybrid, label: "Hybrid", desc: "Rules plus manual pins", icon: Layers },
  ];

  const coverModes: { value: CollectionCoverModeCode; label: string }[] = [
    { value: COLLECTION_COVER_MODE.mosaic, label: "Mosaic" },
    { value: COLLECTION_COVER_MODE.item, label: "Standard" },
  ];

  let hydratedId = $state<string | null>(null);
  let title = $state("");
  let description = $state("");
  let mode = $state<CollectionModeCode>(COLLECTION_MODE.manual);
  let coverMode = $state<CollectionCoverModeCode>(COLLECTION_COVER_MODE.mosaic);
  let isNsfw = $state(false);
  let isShared = $state(false);
  let ruleTree = $state<CollectionRuleGroup>({ ...EMPTY_COLLECTION_RULE, children: [] });
  let saving = $state(false);
  let saveError = $state<string | null>(null);
  let previewing = $state(false);
  let previewError = $state<string | null>(null);
  let previewTotal = $state<number | null>(null);
  let previewCards = $state<EntityThumbnailCard[]>([]);
  let libraryRoots = $state<LibraryRoot[]>([]);
  let previewToken = 0;

  const showRules = $derived(mode === COLLECTION_MODE.dynamic || mode === COLLECTION_MODE.hybrid);
  const hasConditions = $derived(ruleTree.children.length > 0);
  const rulesReady = $derived(rulesReadyForPreview(ruleTree));
  const canSave = $derived(title.trim().length > 0 && !saving && (!showRules || rulesReady));
  const libraryOptions = $derived(
    libraryRoots
      .filter((root) => root.enabled !== false)
      .filter((root) => nsfw.mode === "show" || root.isNsfw !== true)
      .map((root) => ({
        value: root.id,
        label: root.label?.trim() || root.path,
      })),
  );
  const previewSummary = $derived.by(() => {
    if (!hasConditions) return "Add rules to preview";
    if (!rulesReady) return "Fill in rule values";
    if (previewing && previewCards.length === 0) return "Building preview";
    if (previewTotal === null) return "Ready to preview";
    return `${previewTotal} matching ${previewTotal === 1 ? "item" : "items"}`;
  });

  onMount(() => {
    const controller = new AbortController();
    void loadLibraryRoots(controller.signal);
    return () => controller.abort();
  });

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
      mode = COLLECTION_MODE.manual;
      coverMode = COLLECTION_COVER_MODE.mosaic;
      isNsfw = false;
      isShared = false;
      ruleTree = { ...EMPTY_COLLECTION_RULE, children: [] };
      resetPreview();
      return;
    }

    title = collection.title;
    description = getDescription(collection.capabilities) ?? "";
    const configuration = getCollectionConfigurationCapability(collection.capabilities);
    mode = normalizeMode(configuration?.mode);
    coverMode = normalizeCoverMode(configuration?.coverMode);
    isNsfw = hasNsfw(collection.capabilities);
    isShared = configuration?.isShared === true;
    ruleTree = parseRuleTree(configuration?.ruleTreeJson);
  });

  $effect(() => {
    const snapshot = JSON.stringify(ruleTree);
    const active = showRules && hasConditions;
    const ready = rulesReady;
    void snapshot;

    // Invalidate outstanding requests as soon as the rule changes, not after the debounce.
    previewToken += 1;
    if (!active || !ready) {
      resetPreview();
      return;
    }


    const timer = setTimeout(() => {
      void runPreview();
    }, 500);

    return () => { clearTimeout(timer); previewToken += 1; };
  });

  function normalizeMode(value: string | null | undefined): CollectionModeCode {
    return value === COLLECTION_MODE.dynamic || value === COLLECTION_MODE.hybrid
      ? value
      : COLLECTION_MODE.manual;
  }

  function normalizeCoverMode(value: string | null | undefined): CollectionCoverModeCode {
    return value === COLLECTION_COVER_MODE.custom || value === COLLECTION_COVER_MODE.item
      ? value
      : COLLECTION_COVER_MODE.mosaic;
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

  function resetPreview() {
    previewTotal = null;
    previewCards = [];
    previewError = null;
    previewing = false;
  }

  async function loadLibraryRoots(signal?: AbortSignal) {
    try {
      libraryRoots = await fetchLibraryRoots({ signal });
    } catch (err) {
      if (err instanceof DOMException && err.name === "AbortError") return;
      libraryRoots = [];
    }
  }

  function buildRequest(): CollectionWriteRequest {
    return {
      title: title.trim(),
      description: description.trim() ? description.trim() : null,
      mode,
      ruleTreeJson: showRules ? JSON.stringify(ruleTree) : null,
      coverMode,
      coverItemId: collection ? getCoverSelectionCapability(collection.capabilities)?.entityId ?? null : null,
      isNsfw,
      isShared,
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
      await goto(resolve(`/collections/${saved.id}` as "/"));
    } catch (err) {
      saveError = err instanceof Error ? err.message : "Failed to save collection.";
    } finally {
      saving = false;
    }
  }

  async function runPreview() {
    if (!showRules || !hasConditions || !rulesReady) return;
    const token = ++previewToken;
    previewing = true;
    previewError = null;
    try {
      const preview = await previewCollectionRules(JSON.stringify(ruleTree));
      if (token !== previewToken) return;
      previewTotal = preview.total;
      const nextCards: EntityThumbnailCard[] = [];
      for (const item of preview.sample) {
        if (!item.entity) continue;
        try {
          nextCards.push(entityCardToThumbnailCard(item.entity));
        } catch {
          // Ignore malformed preview entries so the editor remains usable.
        }
      }
      previewCards = nextCards;
    } catch (err) {
      if (token !== previewToken) return;
      previewError = friendlyPreviewError(err);
    } finally {
      if (token === previewToken) previewing = false;
    }
  }

  function friendlyPreviewError(err: unknown): string {
    const message = err instanceof Error ? err.message : String(err);
    if (/failed to fetch|networkerror|load failed/i.test(message)) {
      return "Preview service is unreachable. Check that the backend is running.";
    }
    return message || "Preview failed.";
  }
</script>

<svelte:head>
  <title>{isNew ? "New Collection" : `Edit ${collection?.title ?? "Collection"}`} · Prismedia</title>
</svelte:head>

<section class="grid max-w-[96rem] gap-4">
  <header class="flex flex-wrap items-end justify-between gap-4 border-b border-border-subtle pb-3">
    <div>
      <p class="text-kicker mb-1">Library · Collection</p>
      <h1 class="m-0 font-heading text-[clamp(1.35rem,2vw,2rem)] text-text-primary">
        {isNew ? "New Collection" : "Edit Collection"}
      </h1>
    </div>
    <div class="flex items-center gap-2">
      <a
        href={resolve((collection ? `/collections/${collection.id}` : "/collections") as "/")}
        class={buttonVariants({ variant: "outline", size: "sm" })}
      >
        <XCircle class="h-3.5 w-3.5" />
        Cancel
      </a>
      <Button variant="primary" size="sm"
        type="button"
        disabled={!canSave}
        onclick={save}
        class=""
      >
        {#if saving}
          <Loader2 class="h-3.5 w-3.5 animate-spin" />
        {:else}
          <Save class="h-3.5 w-3.5" />
        {/if}
        {saving ? "Saving..." : "Save"}
      </Button>
    </div>
  </header>

  {#if saveError}
    <div class="flex items-center gap-3 rounded-sm border border-error/50 bg-surface-2 px-4 py-2.5 text-[0.8rem] text-error-text">
      <ShieldAlert class="h-4 w-4 flex-shrink-0" />
      {saveError}
    </div>
  {/if}

  <section class="surface-panel overflow-hidden">
    <div class="grid grid-cols-1 lg:grid-cols-[minmax(0,2fr)_minmax(18rem,1fr)]">
      <div class="grid min-w-0 gap-4 p-4 sm:p-5">
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
          placeholder="What this collection is about..."
          rows={4}
          minHeightRem={5.25}
          disabled={saving}
        />
      </div>

      <aside class="grid content-start gap-4 border-t border-border-subtle bg-surface-1/40 p-4 sm:p-5 lg:border-l lg:border-t-0">
        <div class="flex items-center justify-between gap-3">
          <h2 class="text-kicker m-0 flex items-center gap-1.5">
            <SlidersHorizontal class="h-3 w-3" /> Settings
          </h2>
        </div>
        <div class="flex items-center justify-between gap-4 rounded-sm border border-border-subtle bg-surface-2 p-3">
          <div class="min-w-0">
            <h3 class="text-kicker m-0 flex items-center gap-1.5">
              <Share2 class="h-3 w-3" /> Household Sharing
            </h3>
            <p class="m-0 mt-1 text-[0.68rem] leading-snug text-text-disabled">
              {isShared ? "Visible to every signed-in user" : "Visible only to you"}
            </p>
          </div>
          <Toggle
            checked={isShared}
            disabled={saving}
            onchange={(checked) => (isShared = checked)}
            ariaLabel="Share collection with household users"
          />
        </div>
        <div class="grid gap-2">
          <h3 class="text-kicker m-0 flex items-center gap-1.5">
            <FolderPlus class="h-3 w-3" /> Cover
          </h3>
          <ToggleGroup.Root type="single" variant="outline" disabled={saving} aria-label="Cover mode" class="grid grid-cols-2 gap-1.5"
            bind:value={() => coverMode, next => { const option = coverModes.find(item => item.value === next); if (option) coverMode = option.value; }}>
            {#each coverModes as cm (cm.value)}
              <ToggleGroup.Item value={cm.value}><FolderPlus />{cm.label}</ToggleGroup.Item>
            {/each}
          </ToggleGroup.Root>
        </div>
        <div class="grid gap-2">
          <h3 class="text-kicker m-0">Collection Mode</h3>
          <RadioGroup.Root disabled={saving} aria-label="Collection mode" class="gap-1.5"
            bind:value={() => mode, next => { const option = modes.find(item => item.value === next); if (option) mode = option.value; }}>
            {#each modes as option (option.value)}
              {@const Icon = option.icon}
              <label class="flex cursor-pointer items-start gap-3 rounded-sm border border-border bg-card p-3 has-[[data-state=checked]]:border-ring">
                <RadioGroup.Item value={option.value} aria-label={option.label} class="mt-0.5" />
                <span class="grid gap-1">
                  <span class="flex items-center gap-2 font-heading text-sm font-medium"><Icon class="size-4" />{option.label}</span>
                  <span class="text-xs leading-relaxed text-muted-foreground">{option.desc}</span>
                </span>
              </label>
            {/each}
          </RadioGroup.Root>
        </div>
      </aside>
    </div>
  </section>

  {#if showRules}
    <section class="grid gap-3">
      <div class="surface-panel overflow-hidden">
        <div class="flex flex-wrap items-center justify-between gap-3 border-b border-border-subtle px-4 py-3">
          <div>
            <h2 class="m-0 font-heading text-base font-semibold">Collection rules</h2>
            <p class="mt-1 text-sm text-muted-foreground" role="status">{previewSummary}</p>
          </div>
          <div class="flex flex-wrap items-center justify-end gap-2">
            <Button variant="outline" size="sm"
              type="button"
              disabled={previewing || !hasConditions || !rulesReady || saving}
              onclick={() => void runPreview()}
              title="Refresh preview"
              class=""
            >
              {#if previewing}
                <Loader2 class="h-3 w-3 animate-spin" />
              {:else}
                <Eye class="h-3 w-3" />
              {/if}
              {previewing ? "Running" : "Refresh"}
            </Button>
          </div>
        </div>

        <div class="p-4">
          <ConditionBuilder
            rule={ruleTree}
            {libraryOptions}
            onChange={(next) => (ruleTree = next)}
            disabled={saving}
          />
        </div>
      </div>

      {#if previewError}
        <div class="flex items-center gap-3 rounded-sm border border-error/50 bg-surface-2 px-4 py-2.5 text-[0.8rem] text-error-text">
          <ShieldAlert class="h-4 w-4 flex-shrink-0" />
          <span class="flex-1">{previewError}</span>
          <Button variant="outline" size="sm"
            type="button"

            onclick={() => (previewError = null)}
          >
            Dismiss
          </Button>
        </div>
      {/if}

      {#if !rulesReady}
        <StatePlaceholder icon={SlidersHorizontal}
          title={hasConditions ? "Complete your conditions" : "Build a collection rule"}
          description={hasConditions ? "Enter the missing values to preview matching items." : "Add a condition to choose what belongs in this collection."} />
      {:else}
      <EntityGrid
        selectable={false}
        cards={previewCards}
        dockControls={false}
        emptyTitle={hasConditions && rulesReady ? "No matching items" : "Rule preview"}
        emptyMessage={hasConditions && rulesReady ? "No items match the current rule set." : "No preview sample is available."}
        initialPageSize={48}
        initialSortBy="kind"
        loading={previewing && previewCards.length === 0}
        pageSizeOptions={[24, 48, 96]}
        prefsKey="collection-rule-preview"
        showPagination={previewCards.length > 0}
      />
      {/if}
    </section>
  {/if}
</section>
