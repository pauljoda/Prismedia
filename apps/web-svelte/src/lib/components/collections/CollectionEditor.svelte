<script lang="ts">
  import { goto } from "$app/navigation";
  import {
    Eye,
    FolderPlus,
    Layers,
    List,
    Loader2,
    Save,
    ShieldAlert,
    SlidersHorizontal,
    Type,
    XCircle,
    Zap,
  } from "@lucide/svelte";
  import type { Component } from "svelte";
  import { cn } from "@prismedia/ui-svelte";
  import type { CollectionDetail } from "$lib/api/generated/model";
  import { createCollection, previewCollectionRules, updateCollection } from "$lib/api/collections";
  import { getDescription, isNsfw as hasNsfw } from "$lib/api/capabilities";
  import {
    COLLECTION_RULE_FIELDS,
    EMPTY_COLLECTION_RULE,
    type CollectionConditionValue,
    type CollectionCoverMode,
    type CollectionMode,
    type CollectionOperator,
    type CollectionRuleCondition,
    type CollectionRuleFieldDef,
    type CollectionRuleGroup,
    type CollectionWriteRequest,
  } from "$lib/collections/models";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import TextAreaField from "$lib/components/forms/TextAreaField.svelte";
  import TextField from "$lib/components/forms/TextField.svelte";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";
  import ConditionBuilder from "./ConditionBuilder.svelte";

  interface Props {
    collection?: CollectionDetail | null;
    isNew?: boolean;
  }

  let { collection = null, isNew = false }: Props = $props();

  const appChrome = useAppChrome();
  const modes: { value: CollectionMode; label: string; desc: string; icon: Component }[] = [
    { value: "manual", label: "Manual", desc: "Hand-pick and order items", icon: List },
    { value: "dynamic", label: "Dynamic", desc: "Auto-populate from rules", icon: Zap },
    { value: "hybrid", label: "Hybrid", desc: "Rules plus manual pins", icon: Layers },
  ];

  const coverModes: { value: CollectionCoverMode; label: string }[] = [
    { value: "mosaic", label: "Mosaic" },
    { value: "item", label: "Standard" },
  ];

  let hydratedId = $state<string | null>(null);
  let title = $state("");
  let description = $state("");
  let mode = $state<CollectionMode>("manual");
  let coverMode = $state<CollectionCoverMode>("mosaic");
  let isNsfw = $state(false);
  let ruleTree = $state<CollectionRuleGroup>({ ...EMPTY_COLLECTION_RULE, children: [] });
  let saving = $state(false);
  let saveError = $state<string | null>(null);
  let previewing = $state(false);
  let previewError = $state<string | null>(null);
  let previewTotal = $state<number | null>(null);
  let previewByType = $state<Record<string, number>>({});
  let previewCards = $state<EntityThumbnailCard[]>([]);
  let previewToken = 0;

  const showRules = $derived(mode === "dynamic" || mode === "hybrid");
  const canSave = $derived(title.trim().length > 0 && !saving);
  const hasConditions = $derived(ruleTree.children.length > 0);
  const rulesReady = $derived(allConditionsRunnable(ruleTree));
  const previewSummary = $derived.by(() => {
    if (!hasConditions) return "Add rules to preview";
    if (!rulesReady) return "Fill in rule values";
    if (previewing && previewCards.length === 0) return "Building preview";
    if (previewTotal === null) return "Ready to preview";
    return `${previewTotal} matching ${previewTotal === 1 ? "item" : "items"}`;
  });

  function isNullaryOperator(op: CollectionOperator): boolean {
    return op === "is_null" || op === "is_not_null" || op === "is_true" || op === "is_false";
  }

  function findField(fieldName: string): CollectionRuleFieldDef | null {
    return COLLECTION_RULE_FIELDS.find((field) => field.field === fieldName) ?? null;
  }

  function isConditionRunnable(condition: CollectionRuleCondition): boolean {
    const field = findField(condition.field);
    if (!field) return false;
    const op = condition.operator;
    if (isNullaryOperator(op)) return true;
    const value = condition.value as CollectionConditionValue;
    if (value === null || value === undefined) return false;
    if (op === "between") {
      if (!Array.isArray(value) || value.length !== 2) return false;
      if (field.fieldType === "date") {
        return !Number.isNaN(new Date(String(value[0])).getTime()) &&
          !Number.isNaN(new Date(String(value[1])).getTime());
      }
      return Number.isFinite(Number(value[0])) && Number.isFinite(Number(value[1]));
    }
    if (op === "in" || op === "not_in") {
      return Array.isArray(value) && value.length > 0;
    }
    if (field.fieldType === "number") {
      return typeof value === "number" && Number.isFinite(value);
    }
    if (field.fieldType === "date") {
      if (typeof value !== "string" || value.length === 0) return false;
      return !Number.isNaN(new Date(value).getTime());
    }
    if (field.fieldType === "enum") {
      return typeof value === "string" && value.length > 0;
    }
    return typeof value === "string" || typeof value === "number" || typeof value === "boolean";
  }

  function allConditionsRunnable(rule: CollectionRuleGroup): boolean {
    for (const child of rule.children) {
      if (child.type === "condition" && !isConditionRunnable(child)) return false;
    }
    return true;
  }

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
      isNsfw = false;
      ruleTree = { ...EMPTY_COLLECTION_RULE, children: [] };
      resetPreview();
      return;
    }

    title = collection.title;
    description = getDescription(collection.capabilities) ?? "";
    mode = normalizeMode(collection.mode);
    coverMode = normalizeCoverMode(collection.coverMode);
    isNsfw = hasNsfw(collection.capabilities);
    ruleTree = parseRuleTree(collection.ruleTreeJson);
  });

  $effect(() => {
    const snapshot = JSON.stringify(ruleTree);
    const active = showRules && hasConditions;
    const ready = rulesReady;
    void snapshot;

    if (!active) {
      resetPreview();
      return;
    }

    if (!ready) return;

    const timer = setTimeout(() => {
      void runPreview();
    }, 500);

    return () => clearTimeout(timer);
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

  function resetPreview() {
    previewTotal = null;
    previewByType = {};
    previewCards = [];
    previewError = null;
    previewing = false;
  }

  function buildRequest(): CollectionWriteRequest {
    return {
      title: title.trim(),
      description: description.trim() ? description.trim() : null,
      mode,
      ruleTreeJson: showRules ? JSON.stringify(ruleTree) : null,
      coverMode,
      coverItemId: collection?.coverItemId ?? null,
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

  async function runPreview() {
    if (!showRules || !hasConditions || !rulesReady) return;
    const token = ++previewToken;
    previewing = true;
    previewError = null;
    try {
      const preview = await previewCollectionRules(JSON.stringify(ruleTree));
      if (token !== previewToken) return;
      previewTotal = preview.total;
      previewByType = Object.fromEntries(
        Object.entries(preview.byType).filter(([, value]) => typeof value === "number"),
      ) as Record<string, number>;
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
        href={collection ? `/collections/${collection.id}` : "/collections"}
        class={cn(
          "inline-flex items-center gap-1.5 rounded-sm border border-border-subtle bg-surface-2 px-3 py-2 text-[0.78rem] text-text-muted no-underline transition-colors",
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
          "inline-flex items-center gap-1.5 rounded-sm border border-border-accent bg-gradient-to-r from-accent-900 via-accent-800 to-accent-900 px-4 py-2 text-[0.78rem] font-medium text-accent-100 shadow-[var(--shadow-glow-accent)] transition-all",
          "hover:shadow-[var(--shadow-glow-accent-strong)]",
          "disabled:cursor-not-allowed disabled:opacity-50 disabled:shadow-none",
        )}
      >
        {#if saving}
          <Loader2 class="h-3.5 w-3.5 animate-spin" />
        {:else}
          <Save class="h-3.5 w-3.5" />
        {/if}
        {saving ? "Saving..." : "Save"}
      </button>
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
        <div class="grid gap-2">
          <h3 class="text-kicker m-0 flex items-center gap-1.5">
            <FolderPlus class="h-3 w-3" /> Cover
          </h3>
          <div class="grid grid-cols-2 gap-1.5" role="radiogroup" aria-label="Cover mode">
            {#each coverModes as cm (cm.value)}
              {@const active = coverMode === cm.value}
              <button
                type="button"
                role="radio"
                aria-checked={active}
                disabled={saving}
                onclick={() => (coverMode = cm.value)}
                class={cn(
                  "inline-flex h-8 items-center justify-center gap-1.5 rounded-xs border px-3 text-[0.7rem] font-medium transition-all",
                  "disabled:cursor-not-allowed disabled:opacity-50",
                  active
                    ? "border-border-accent-strong bg-accent-950/30 text-text-accent shadow-[0_0_10px_rgba(242,194,106,0.10)]"
                    : "border-border-subtle bg-surface-2 text-text-muted hover:border-border-default hover:text-text-primary",
                )}
              >
                <FolderPlus class="h-3 w-3" />
                {cm.label}
              </button>
            {/each}
          </div>
        </div>
        <div class="grid gap-2">
          <h3 class="text-kicker m-0">Collection Mode</h3>
          <div class="grid gap-1.5" role="radiogroup" aria-label="Collection mode">
            {#each modes as option (option.value)}
              {@const active = mode === option.value}
              {@const Icon = option.icon}
              <button
                type="button"
                role="radio"
                aria-checked={active}
                disabled={saving}
                onclick={() => (mode = option.value)}
                class={cn(
                  "group relative grid gap-1 overflow-hidden rounded-sm border p-3 text-left transition-all duration-normal",
                  "disabled:cursor-not-allowed disabled:opacity-50",
                  active
                    ? "border-border-accent-strong bg-gradient-to-br from-accent-950/40 to-accent-950/10 shadow-[var(--shadow-glow-accent)]"
                    : "border-border-subtle bg-surface-2 hover:border-border-default",
                )}
              >
                <span class="relative flex items-center gap-1.5">
                  <Icon class={cn("h-3.5 w-3.5 transition-colors", active ? "text-text-accent" : "text-text-muted")} />
                  <span
                    class={cn(
                      "font-heading text-[0.85rem] font-semibold transition-colors",
                      active ? "text-text-accent" : "text-text-primary",
                    )}
                  >
                    {option.label}
                  </span>
                  {#if active}
                    <span class="ml-auto font-mono text-[0.55rem] font-bold uppercase tracking-[0.18em] text-text-accent/80">
                      Active
                    </span>
                  {/if}
                </span>
                <span class="relative text-[0.68rem] leading-snug text-text-disabled">
                  {option.desc}
                </span>
              </button>
            {/each}
          </div>
        </div>
      </aside>
    </div>
  </section>

  {#if showRules}
    <section class="grid gap-3">
      <div class="surface-panel overflow-hidden">
        <div class="flex flex-wrap items-center justify-between gap-3 border-b border-border-subtle px-4 py-3">
          <div>
            <p class="text-kicker m-0 flex items-center gap-1.5">
              <SlidersHorizontal class="h-3 w-3" /> Rule Editor
            </p>
            <p class="m-0 mt-1 text-[0.75rem] text-text-muted">{previewSummary}</p>
          </div>
          <div class="flex flex-wrap items-center justify-end gap-2">
            {#if previewTotal !== null && Object.keys(previewByType).length > 0}
              <div class="flex flex-wrap justify-end gap-1">
                {#each Object.entries(previewByType) as [kind, count] (kind)}
                  <span
                    class="inline-flex items-center gap-1 rounded-xs border border-border-subtle bg-surface-2/70 px-2 py-1 font-mono text-[0.6rem] uppercase tracking-wider text-text-muted tabular-nums"
                  >
                    <span>{kind}</span>
                    <strong class="text-text-accent">{count}</strong>
                  </span>
                {/each}
              </div>
            {/if}
            <button
              type="button"
              disabled={previewing || !hasConditions || !rulesReady || saving}
              onclick={() => void runPreview()}
              title="Refresh preview"
              class={cn(
                "inline-flex h-8 items-center gap-1.5 rounded-xs border border-border-subtle bg-surface-2 px-3 font-mono text-[0.65rem] uppercase tracking-wider text-text-muted transition-colors",
                "hover:border-border-accent hover:text-text-accent",
                "disabled:cursor-not-allowed disabled:opacity-40",
              )}
            >
              {#if previewing}
                <Loader2 class="h-3 w-3 animate-spin" />
              {:else}
                <Eye class="h-3 w-3" />
              {/if}
              {previewing ? "Running" : "Refresh"}
            </button>
          </div>
        </div>

        <div class="p-4">
          <ConditionBuilder rule={ruleTree} onChange={(next) => (ruleTree = next)} disabled={saving} />
        </div>
      </div>

      {#if previewError}
        <div class="flex items-center gap-3 rounded-sm border border-error/50 bg-surface-2 px-4 py-2.5 text-[0.8rem] text-error-text">
          <ShieldAlert class="h-4 w-4 flex-shrink-0" />
          <span class="flex-1">{previewError}</span>
          <button
            type="button"
            class="inline-flex h-7 items-center rounded-xs border border-border-subtle bg-surface-1 px-2 font-mono text-[0.62rem] uppercase tracking-wider text-text-muted transition-colors hover:border-border-accent hover:text-text-accent"
            onclick={() => (previewError = null)}
          >
            Dismiss
          </button>
        </div>
      {/if}

      <EntityGrid
        cards={previewCards}
        dockControls={false}
        emptyTitle={hasConditions && rulesReady ? "No matching items" : "Rule preview"}
        emptyMessage={hasConditions && rulesReady ? "No items match the current rule set." : "No preview sample is available."}
        initialPageSize={48}
        initialSortBy="kind"
        loading={previewing && previewCards.length === 0}
        pageSizeOptions={[24, 48, 96]}
        prefsKey="collection-rule-preview"
        selectable={false}
        showPagination={previewCards.length > 0}
      />
    </section>
  {/if}
</section>
