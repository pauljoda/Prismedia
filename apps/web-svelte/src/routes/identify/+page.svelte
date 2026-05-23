<script lang="ts">
  import { onDestroy, onMount } from "svelte";
  import {
    AlertCircle,
    Check,
    ChevronRight,
    Images,
    KeyRound,
    Loader2,
    PanelRightClose,
    RefreshCw,
    ScanSearch,
    Search,
    X,
  } from "@lucide/svelte";
  import {
    applyIdentifyProposal,
    closeBulkIdentifySession,
    fetchBulkIdentifySession,
    fetchIdentifyEntities,
    fetchIdentifyProviders,
    fetchPluginProviders,
    identifyEntity,
    installPlugin,
    savePluginAuth,
    startBulkIdentify,
    type EntityMetadataProposal,
    type EntitySearchCandidate,
    type ImageCandidate,
    type IdentifyBulkSession,
    type PluginProvider,
  } from "$lib/api/identify";
  import { reviewChildProposals } from "$lib/components/identify-review";
  import type { EntityCard } from "$lib/api/prismedia";

  type IdentifyKind = "video" | "video-series";

  const FIELD_KEYS = [
    "title",
    "description",
    "externalIds",
    "urls",
    "tags",
    "studio",
    "credits",
    "dates",
    "stats",
    "positions",
    "classification",
    "images",
  ] as const;

  const FIELD_LABELS: Record<(typeof FIELD_KEYS)[number], string> = {
    title: "Title",
    description: "Description",
    externalIds: "Provider IDs",
    urls: "Links",
    tags: "Tags",
    studio: "Studio",
    credits: "Credits",
    dates: "Dates",
    stats: "Stats",
    positions: "Positions",
    classification: "Classification",
    images: "Artwork",
  };

  const KIND_LABELS: Record<IdentifyKind, string> = {
    video: "Movies",
    "video-series": "Series",
  };

  let kind = $state<IdentifyKind>("video");
  let query = $state("");
  let providers = $state<PluginProvider[]>([]);
  let providerId = $state<string | null>(null);
  let entities = $state<EntityCard[]>([]);
  let selectedEntityIds = $state<string[]>([]);
  let activeEntity = $state<EntityCard | null>(null);
  let proposal = $state<EntityMetadataProposal | null>(null);
  let selectedFields = $state<Record<string, boolean>>({});
  let selectedImages = $state<Record<string, string | null>>({});
  let loading = $state(true);
  let identifyingId = $state<string | null>(null);
  let applying = $state(false);
  let authValues = $state<Record<string, string>>({});
  let authSaving = $state<string | null>(null);
  let bulkSession = $state<IdentifyBulkSession | null>(null);
  let bulkStarting = $state(false);
  let error = $state<string | null>(null);
  let message = $state<string | null>(null);
  let pollTimer: ReturnType<typeof setTimeout> | null = null;

  const availableProviders = $derived(
    providers.filter((provider) =>
      provider.supports.some((support) => support.entityKind === kind),
    ),
  );
  const selectedProvider = $derived.by(() =>
    availableProviders.find((provider) => provider.id === providerId) ?? availableProviders[0] ?? null,
  );
  const canIdentify = $derived(Boolean(selectedProvider && activeEntity && !selectedProvider.missingAuthKeys.length));
  const bulkResults = $derived(bulkSession?.results ?? []);
  const proposalReviewChildren = $derived(proposal ? reviewChildProposals(proposal) : []);

  onMount(() => {
    void load();
  });

  onDestroy(() => {
    if (pollTimer) clearTimeout(pollTimer);
  });

  async function load() {
    loading = true;
    error = null;
    try {
      const [providerRows, entityRows] = await Promise.all([
        fetchPluginProviders(),
        fetchIdentifyEntities(kind, query),
      ]);
      providers = providerRows;
      entities = entityRows.items;
      selectedEntityIds = selectedEntityIds.filter((id) =>
        entityRows.items.some((entity) => entity.id === id),
      );
    } catch (err) {
      error = readError(err);
    } finally {
      loading = false;
    }
  }

  async function refreshEntities() {
    error = null;
    try {
      const entityRows = await fetchIdentifyEntities(kind, query);
      entities = entityRows.items;
      selectedEntityIds = selectedEntityIds.filter((id) =>
        entityRows.items.some((entity) => entity.id === id),
      );
    } catch (err) {
      error = readError(err);
    }
  }

  async function install(provider: PluginProvider) {
    error = null;
    try {
      const installed = await installPlugin(provider.id);
      providers = providers.map((row) => (row.id === installed.id ? installed : row));
      providerId = installed.id;
      message = `${installed.name} installed`;
    } catch (err) {
      error = readError(err);
    }
  }

  async function saveAuth(provider: PluginProvider) {
    authSaving = provider.id;
    error = null;
    try {
      const values: Record<string, string | null> = {};
      for (const field of provider.auth) {
        values[field.key] = authValues[`${provider.id}:${field.key}`] ?? null;
      }

      await savePluginAuth(provider.id, values);
      providers = await fetchIdentifyProviders(kind);
      providerId = provider.id;
      message = `${provider.name} credentials saved`;
    } catch (err) {
      error = readError(err);
    } finally {
      authSaving = null;
    }
  }

  async function runIdentify(entity: EntityCard, candidate?: EntitySearchCandidate) {
    const provider = selectedProvider;
    if (!provider) return;
    activeEntity = entity;
    identifyingId = entity.id;
    error = null;
    try {
      const result = await identifyEntity(entity.id, provider.id, candidate
        ? { externalIds: candidate.externalIds }
        : undefined);
      openProposal(result);
    } catch (err) {
      error = readError(err);
    } finally {
      identifyingId = null;
    }
  }

  function openProposal(result: EntityMetadataProposal) {
    proposal = result;
    selectedFields = Object.fromEntries(
      FIELD_KEYS.map((key) => [key, hasField(result, key)]),
    );
    selectedImages = defaultImageSelection(result.images);
  }

  async function applyProposal(closeAfter = true) {
    if (!activeEntity || !proposal) return;
    applying = true;
    error = null;
    try {
      const fields = Object.entries(selectedFields)
        .filter(([, enabled]) => enabled)
        .map(([field]) => field);
      const updated = await applyIdentifyProposal(
        activeEntity.id,
        proposal,
        fields,
        selectedImages,
      );
      entities = entities.map((entity) => (entity.id === updated.id ? updated : entity));
      activeEntity = updated;
      message = `${updated.title} updated`;
      if (closeAfter) closeReview();
    } catch (err) {
      error = readError(err);
    } finally {
      applying = false;
    }
  }

  async function startBulk() {
    const provider = selectedProvider;
    if (!provider || selectedEntityIds.length === 0) return;
    bulkStarting = true;
    error = null;
    try {
      bulkSession = await startBulkIdentify(provider.id, selectedEntityIds);
      schedulePoll();
    } catch (err) {
      error = readError(err);
    } finally {
      bulkStarting = false;
    }
  }

  function schedulePoll() {
    if (!bulkSession || bulkSession.status === "completed") return;
    pollTimer = setTimeout(async () => {
      if (!bulkSession) return;
      try {
        bulkSession = await fetchBulkIdentifySession(bulkSession.id);
      } catch (err) {
        error = readError(err);
        return;
      }
      schedulePoll();
    }, 1200);
  }

  async function closeBulk() {
    if (!bulkSession) return;
    const id = bulkSession.id;
    bulkSession = null;
    if (pollTimer) clearTimeout(pollTimer);
    pollTimer = null;
    await closeBulkIdentifySession(id).catch(() => undefined);
  }

  function reviewBulkResult(result: EntityMetadataProposal, entityId: string) {
    const entity = entities.find((row) => row.id === entityId);
    if (!entity) return;
    activeEntity = entity;
    openProposal(result);
  }

  function rerunCandidate(candidate: EntitySearchCandidate) {
    if (!activeEntity) return;
    void runIdentify(activeEntity, candidate);
  }

  function toggleSelected(entityId: string) {
    selectedEntityIds = selectedEntityIds.includes(entityId)
      ? selectedEntityIds.filter((id) => id !== entityId)
      : [...selectedEntityIds, entityId];
  }

  function closeReview() {
    proposal = null;
    activeEntity = null;
    selectedFields = {};
    selectedImages = {};
  }

  function fieldValue(result: EntityMetadataProposal, field: string): string {
    const patch = result.patch;
    if (field === "title") return patch.title ?? "";
    if (field === "description") return patch.description ?? "";
    if (field === "externalIds") return entries(patch.externalIds).join(", ");
    if (field === "urls") return patch.urls.join(", ");
    if (field === "tags") return patch.tags.join(", ");
    if (field === "studio") return patch.studio ?? "";
    if (field === "credits") return patch.credits.map((credit) => credit.character ? `${credit.name} as ${credit.character}` : credit.name).join(", ");
    if (field === "dates") return entries(patch.dates).join(", ");
    if (field === "stats") return entries(patch.stats).join(", ");
    if (field === "positions") return entries(patch.positions).join(", ");
    if (field === "classification") return patch.classification ?? "";
    if (field === "images") return result.images.length > 0 ? `${result.images.length} candidate${result.images.length === 1 ? "" : "s"}` : "";
    return "";
  }

  function hasField(result: EntityMetadataProposal, field: string): boolean {
    const value = fieldValue(result, field);
    return value.trim().length > 0;
  }

  function imageGroups(images: ImageCandidate[]): Array<{ kind: string; images: ImageCandidate[] }> {
    const groups: Record<string, ImageCandidate[]> = {};
    for (const image of images) {
      groups[image.kind] = [...(groups[image.kind] ?? []), image];
    }
    return Object.entries(groups).map(([groupKind, rows]) => ({ kind: groupKind, images: rows }));
  }

  function defaultImageSelection(images: ImageCandidate[]): Record<string, string | null> {
    const selected: Record<string, string | null> = {};
    for (const group of imageGroups(images)) {
      selected[group.kind] = group.images[0]?.url ?? null;
    }
    return selected;
  }

  function entries(record: Record<string, string | number>): string[] {
    return Object.entries(record).map(([key, value]) => `${key}: ${value}`);
  }

  function readError(err: unknown): string {
    if (!(err instanceof Error)) return "Request failed";
    try {
      const parsed = JSON.parse(err.message) as { message?: string; detail?: string };
      return parsed.message ?? parsed.detail ?? err.message;
    } catch {
      return err.message;
    }
  }
</script>

<svelte:head>
  <title>Identify · Prismedia</title>
</svelte:head>

<section class="identify-page">
  <header class="page-header">
    <div>
      <h1>
        <ScanSearch class="h-5 w-5 text-text-accent" />
        Identify
      </h1>
      <p>Provider IDs first, title search only when needed.</p>
    </div>
    <button type="button" class="icon-button" onclick={() => void load()} aria-label="Refresh identify data">
      {#if loading}
        <Loader2 class="h-4 w-4 animate-spin" />
      {:else}
        <RefreshCw class="h-4 w-4" />
      {/if}
    </button>
  </header>

  {#if error}
    <div class="notice error" role="alert">
      <AlertCircle class="h-4 w-4" />
      <span>{error}</span>
      <button type="button" class="icon-button small" onclick={() => (error = null)} aria-label="Dismiss error">
        <X class="h-3.5 w-3.5" />
      </button>
    </div>
  {/if}

  {#if message}
    <div class="notice success">
      <Check class="h-4 w-4" />
      <span>{message}</span>
      <button type="button" class="icon-button small" onclick={() => (message = null)} aria-label="Dismiss message">
        <X class="h-3.5 w-3.5" />
      </button>
    </div>
  {/if}

  <div class="controls">
    <div class="segmented" aria-label="Entity kind">
      {#each Object.entries(KIND_LABELS) as [kindCode, label] (kindCode)}
        <button
          type="button"
          class:active={kind === kindCode}
          onclick={() => {
            kind = kindCode as IdentifyKind;
            selectedEntityIds = [];
            activeEntity = null;
            proposal = null;
            void load();
          }}
        >
          {label}
        </button>
      {/each}
    </div>

    <label class="search-box">
      <Search class="h-4 w-4" />
      <input
        placeholder="Search"
        bind:value={query}
        onkeydown={(event) => {
          if (event.key === "Enter") void refreshEntities();
        }}
      />
    </label>

    <button type="button" class="action-button" onclick={() => void refreshEntities()}>
      <Search class="h-4 w-4" />
      Search
    </button>
  </div>

  <section class="provider-strip">
    {#if availableProviders.length === 0}
      <div class="empty-row">No providers for {KIND_LABELS[kind]}.</div>
    {:else}
      {#each availableProviders as provider (provider.id)}
        <article class:active={provider.id === selectedProvider?.id}>
          <button type="button" class="provider-main" onclick={() => (providerId = provider.id)}>
            <span>{provider.name}</span>
            <small>v{provider.version}</small>
          </button>
          <div class="provider-actions">
            {#if !provider.installed || !provider.enabled}
              <button type="button" class="small-button" onclick={() => void install(provider)}>Install</button>
            {/if}
            {#if provider.auth.length > 0}
              <div class="auth-row">
                <KeyRound class="h-3.5 w-3.5" />
                {#each provider.auth as field (field.key)}
                  <input
                    type="password"
                    placeholder={field.label}
                    bind:value={authValues[`${provider.id}:${field.key}`]}
                  />
                {/each}
                <button
                  type="button"
                  class="small-button"
                  disabled={authSaving === provider.id}
                  onclick={() => void saveAuth(provider)}
                >
                  {#if authSaving === provider.id}
                    <Loader2 class="h-3.5 w-3.5 animate-spin" />
                  {:else}
                    Save
                  {/if}
                </button>
              </div>
            {/if}
          </div>
        </article>
      {/each}
    {/if}
  </section>

  <div class="bulk-bar">
    <span>{selectedEntityIds.length} selected</span>
    <button
      type="button"
      class="action-button"
      disabled={!selectedProvider || selectedEntityIds.length === 0 || bulkStarting}
      onclick={() => void startBulk()}
    >
      {#if bulkStarting}
        <Loader2 class="h-4 w-4 animate-spin" />
      {:else}
        <ScanSearch class="h-4 w-4" />
      {/if}
      Bulk Identify
    </button>
  </div>

  <div class="entity-table">
    {#each entities as entity (entity.id)}
      <article class:active={activeEntity?.id === entity.id}>
        <input
          type="checkbox"
          checked={selectedEntityIds.includes(entity.id)}
          aria-label={`Select ${entity.title}`}
          onchange={() => toggleSelected(entity.id)}
        />
        <button type="button" class="entity-title" onclick={() => (activeEntity = entity)}>
          <span>{entity.title}</span>
          <small>{entity.kind}</small>
        </button>
        <button
          type="button"
          class="action-button"
          disabled={!selectedProvider || identifyingId === entity.id || selectedProvider.missingAuthKeys.length > 0}
          onclick={() => void runIdentify(entity)}
        >
          {#if identifyingId === entity.id}
            <Loader2 class="h-4 w-4 animate-spin" />
          {:else}
            <ChevronRight class="h-4 w-4" />
          {/if}
          Identify
        </button>
      </article>
    {/each}
  </div>

  {#if bulkSession}
    <section class="bulk-results">
      <header>
        <div>
          <h2>Bulk Session</h2>
          <p>{bulkSession.status} · {bulkResults.length}/{bulkSession.entityIds.length}</p>
        </div>
        <button type="button" class="icon-button" onclick={() => void closeBulk()} aria-label="Close bulk session">
          <X class="h-4 w-4" />
        </button>
      </header>
      <div class="result-list">
        {#each bulkResults as result (result.entityId)}
          <button
            type="button"
            disabled={!result.response.ok || !result.response.result}
            onclick={() => result.response.result && reviewBulkResult(result.response.result, result.entityId)}
          >
            <span>{entities.find((entity) => entity.id === result.entityId)?.title ?? result.entityId}</span>
            <small>{result.response.ok ? result.response.result?.matchReason : result.response.error}</small>
          </button>
        {/each}
      </div>
    </section>
  {/if}
</section>

{#if proposal && activeEntity}
  <aside class="review-drawer" aria-label="Review identify result">
    <header>
      <div>
        <p>{proposal.provider} · {proposal.matchReason ?? "match"}</p>
        <h2>{proposal.patch.title ?? activeEntity.title}</h2>
      </div>
      <button type="button" class="icon-button" onclick={closeReview} aria-label="Close review">
        <PanelRightClose class="h-4 w-4" />
      </button>
    </header>

    {#if proposal.candidates.length > 1}
      <section class="drawer-section">
        <h3>Candidates</h3>
        <div class="candidate-list">
          {#each proposal.candidates as candidate (candidate.externalIds.tmdb ?? candidate.title)}
            <button type="button" onclick={() => rerunCandidate(candidate)}>
              {#if candidate.posterUrl}
                <img src={candidate.posterUrl} alt="" />
              {/if}
              <span>{candidate.title}</span>
              <small>{candidate.year ?? ""}</small>
            </button>
          {/each}
        </div>
      </section>
    {/if}

    <section class="drawer-section">
      <h3>Fields</h3>
      <div class="field-list">
        {#each FIELD_KEYS as field (field)}
          {#if hasField(proposal, field)}
            <label>
              <input type="checkbox" bind:checked={selectedFields[field]} />
              <span>{FIELD_LABELS[field]}</span>
              <small>{fieldValue(proposal, field)}</small>
            </label>
          {/if}
        {/each}
      </div>
    </section>

    {#if proposal.images.length > 0}
      <section class="drawer-section">
        <h3>
          <Images class="h-4 w-4" />
          Artwork
        </h3>
        {#each imageGroups(proposal.images) as group (group.kind)}
          <div class="image-group">
            <p>{group.kind}</p>
            <div>
              {#each group.images as image (image.url)}
                <button
                  type="button"
                  class:active={selectedImages[group.kind] === image.url}
                  onclick={() => (selectedImages[group.kind] = image.url)}
                >
                  <img src={image.url} alt="" />
                </button>
              {/each}
            </div>
          </div>
        {/each}
      </section>
    {/if}

    {#if proposalReviewChildren.length > 0}
      <section class="drawer-section">
        <h3>Related Results</h3>
        <div class="children-list">
          {#each proposalReviewChildren as child (child.proposalId)}
            <div>
              <span>{child.patch.title}</span>
              <small>{child.targetKind}</small>
            </div>
          {/each}
        </div>
      </section>
    {/if}

    <footer>
      <button type="button" class="ghost-button" onclick={closeReview}>Reject</button>
      <button type="button" class="action-button primary" disabled={!canIdentify || applying} onclick={() => void applyProposal()}>
        {#if applying}
          <Loader2 class="h-4 w-4 animate-spin" />
        {:else}
          <Check class="h-4 w-4" />
        {/if}
        Apply
      </button>
    </footer>
  </aside>
{/if}

<style>
  .identify-page { display: flex; flex-direction: column; gap: 1rem; padding-bottom: 4rem; }
  .page-header, .bulk-results header { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
  h1, h2, h3 { margin: 0; letter-spacing: 0; }
  h1 { display: flex; align-items: center; gap: 0.65rem; font-size: 1.4rem; }
  h2 { font-size: 1.05rem; }
  h3 { display: flex; align-items: center; gap: 0.45rem; font-size: 0.78rem; text-transform: uppercase; color: var(--color-text-muted); }
  p, small { margin: 0; color: var(--color-text-muted); }
  button, input { border-radius: 0; }
  button { cursor: pointer; }
  button:disabled { cursor: not-allowed; opacity: 0.5; }
  .controls, .bulk-bar, .provider-strip, .entity-table article, .bulk-results, .notice { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-1, #0c1018); }
  .controls { display: grid; grid-template-columns: 1fr; gap: 0.75rem; padding: 0.8rem; }
  .segmented { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); border: 1px solid var(--color-border, #1c2235); }
  .segmented button { border: 0; border-right: 1px solid var(--color-border, #1c2235); background: transparent; color: var(--color-text-muted); padding: 0.6rem; font-size: 0.78rem; }
  .segmented button:last-child { border-right: 0; }
  .segmented button.active, .provider-strip article.active, .entity-table article.active { box-shadow: inset 0 0 0 1px rgba(196, 154, 90, 0.65), 0 0 18px rgba(196, 154, 90, 0.16); }
  .search-box { display: flex; align-items: center; gap: 0.55rem; border: 1px solid var(--color-border, #1c2235); padding: 0 0.65rem; min-height: 2.4rem; }
  .search-box input, .auth-row input { min-width: 0; flex: 1; border: 0; outline: none; background: transparent; color: var(--color-text); font-size: 0.82rem; }
  .action-button, .ghost-button, .small-button, .icon-button { display: inline-flex; align-items: center; justify-content: center; gap: 0.45rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-2, #111827); color: var(--color-text); min-height: 2.4rem; padding: 0 0.8rem; font-size: 0.78rem; }
  .action-button.primary { border-color: rgba(196, 154, 90, 0.75); background: linear-gradient(135deg, rgba(196, 154, 90, 0.24), rgba(196, 154, 90, 0.1)); box-shadow: 0 0 18px rgba(196, 154, 90, 0.16); }
  .ghost-button { background: transparent; }
  .icon-button { width: 2.4rem; padding: 0; }
  .icon-button.small { width: 1.9rem; min-height: 1.9rem; }
  .small-button { min-height: 2rem; padding: 0 0.55rem; }
  .notice { display: flex; align-items: center; gap: 0.6rem; padding: 0.65rem; font-size: 0.82rem; }
  .notice span { flex: 1; }
  .notice.error { border-color: rgba(239, 68, 68, 0.45); }
  .notice.success { border-color: rgba(196, 154, 90, 0.55); }
  .provider-strip { display: grid; grid-template-columns: 1fr; gap: 0.65rem; padding: 0.8rem; }
  .provider-strip article { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-2, #111827); }
  .provider-main { display: flex; align-items: center; justify-content: space-between; width: 100%; border: 0; background: transparent; color: var(--color-text); padding: 0.75rem; text-align: left; }
  .provider-actions { display: flex; flex-direction: column; gap: 0.55rem; padding: 0 0.75rem 0.75rem; }
  .auth-row { display: flex; align-items: center; gap: 0.5rem; border: 1px solid var(--color-border, #1c2235); padding: 0.35rem; }
  .bulk-bar { display: flex; align-items: center; justify-content: space-between; padding: 0.75rem; color: var(--color-text-muted); font-size: 0.78rem; }
  .entity-table { display: grid; gap: 0.55rem; }
  .entity-table article { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: 0.65rem; padding: 0.65rem; }
  .entity-table input[type="checkbox"], .field-list input[type="checkbox"] { width: 1rem; height: 1rem; accent-color: #c49a5a; }
  .entity-title { display: flex; min-width: 0; flex-direction: column; gap: 0.15rem; border: 0; background: transparent; color: var(--color-text); text-align: left; }
  .entity-title span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.88rem; }
  .bulk-results { padding: 0.85rem; }
  .result-list { display: grid; gap: 0.45rem; margin-top: 0.75rem; }
  .result-list button { display: flex; align-items: center; justify-content: space-between; gap: 1rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-2, #111827); color: var(--color-text); padding: 0.6rem; text-align: left; }
  .review-drawer { position: fixed; inset: 0 0 0 auto; z-index: 60; display: flex; width: min(100vw, 520px); flex-direction: column; gap: 1rem; overflow: auto; border-left: 1px solid var(--color-border, #1c2235); background: rgba(9, 12, 18, 0.94); padding: 1rem; backdrop-filter: blur(18px); }
  .review-drawer header, .review-drawer footer { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }
  .review-drawer footer { position: sticky; bottom: -1rem; margin: auto -1rem -1rem; border-top: 1px solid var(--color-border, #1c2235); background: rgba(9, 12, 18, 0.96); padding: 1rem; }
  .drawer-section { display: grid; gap: 0.7rem; }
  .field-list { display: grid; gap: 0.45rem; }
  .field-list label { display: grid; grid-template-columns: auto 7rem minmax(0, 1fr); gap: 0.55rem; align-items: start; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-1, #0c1018); padding: 0.65rem; }
  .field-list span { font-size: 0.78rem; color: var(--color-text); }
  .field-list small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .candidate-list { display: grid; grid-template-columns: repeat(auto-fill, minmax(8rem, 1fr)); gap: 0.55rem; }
  .candidate-list button { display: grid; gap: 0.4rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-1, #0c1018); color: var(--color-text); padding: 0.45rem; text-align: left; }
  .candidate-list img { width: 100%; aspect-ratio: 2 / 3; object-fit: cover; }
  .image-group { display: grid; gap: 0.45rem; }
  .image-group > div { display: grid; grid-template-columns: repeat(auto-fill, minmax(5rem, 1fr)); gap: 0.45rem; }
  .image-group button { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-1, #0c1018); padding: 0.25rem; }
  .image-group button.active { border-color: rgba(196, 154, 90, 0.8); box-shadow: 0 0 16px rgba(196, 154, 90, 0.2); }
  .image-group img { width: 100%; aspect-ratio: 2 / 3; object-fit: cover; }
  .children-list { display: grid; gap: 0.4rem; }
  .children-list div { display: flex; justify-content: space-between; gap: 1rem; border: 1px solid var(--color-border, #1c2235); padding: 0.5rem; }
  .empty-row { color: var(--color-text-muted); font-size: 0.82rem; }

  @media (min-width: 760px) {
    .controls { grid-template-columns: auto minmax(16rem, 1fr) auto; align-items: center; }
    .provider-strip { grid-template-columns: repeat(auto-fit, minmax(18rem, 1fr)); }
  }
</style>
