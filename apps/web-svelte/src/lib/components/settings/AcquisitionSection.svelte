<script lang="ts">
  import { onMount } from "svelte";
  import { Boxes, Loader2, Pencil, PlugZap, Plus, Trash2 } from "@lucide/svelte";
  import { Badge, Button, Checkbox, Panel, Select, StatusLed, TextInput } from "@prismedia/ui-svelte";
  import { INDEXER_KIND, DOWNLOAD_CLIENT_KIND, IMPORT_MODE, FULFILLMENT_MODE, REQUEST_MEDIA_KIND, BLOCKLIST_REASON } from "$lib/api/generated/codes";
  import { cn } from "@prismedia/ui-svelte";
  import type {
    AcquisitionBlocklistEntry,
    BookAcquisitionProfileSaveRequest,
    BookAcquisitionProfileView,
    DownloadClientSaveRequest,
    DownloadClientSummary,
    IndexerConfigSaveRequest,
    IndexerConfigSummary,
  } from "$lib/api/generated/model";
  import {
    deleteAcquisitionProfileConfig,
    deleteBlocklistEntry,
    deleteDownloadClientConfig,
    deleteIndexerConfig,
    fetchAcquisitionProfiles,
    fetchBlocklist,
    fetchDownloadClients,
    fetchIndexers,
    saveAcquisitionProfile,
    saveDownloadClientConfig,
    saveIndexerConfig,
    testDownloadClientConnection,
    testIndexerConnection,
  } from "$lib/api/acquisitions";
  import { fetchLibraryConfig, updateSetting, type LibraryRoot } from "$lib/api/settings";
  import { findSetting, settingKeys, valueAsStringList } from "$lib/settings/app-settings";

  interface Props {
    onError: (msg: string) => void;
    onMessage: (msg: string) => void;
  }
  let { onError, onMessage }: Props = $props();

  const DEFAULT_PATH_TEMPLATE = "{Author}/{Title} ({Year})/{Title}{ - Volume}.{ext}";

  let indexers = $state<IndexerConfigSummary[]>([]);
  let downloadClients = $state<DownloadClientSummary[]>([]);
  let profiles = $state<BookAcquisitionProfileView[]>([]);
  let blocklist = $state<AcquisitionBlocklistEntry[]>([]);
  let bookRoots = $state<LibraryRoot[]>([]);
  let routing = $state<Record<string, string>>({});
  let loading = $state(true);
  let busy = $state(false);

  const ROUTING_KINDS = [
    { kind: REQUEST_MEDIA_KIND.book, label: "Books" },
    { kind: REQUEST_MEDIA_KIND.movie, label: "Movies" },
    { kind: REQUEST_MEDIA_KIND.series, label: "Series" },
    { kind: REQUEST_MEDIA_KIND.artist, label: "Artists" },
    { kind: REQUEST_MEDIA_KIND.album, label: "Albums" },
  ];

  // Inline edit forms (null = closed).
  let indexerForm = $state<IndexerConfigSaveRequest | null>(null);
  let indexerCategories = $state("7000,8000");
  let clientForm = $state<DownloadClientSaveRequest | null>(null);
  let profileForm = $state<BookAcquisitionProfileSaveRequest | null>(null);

  const importModeOptions = [
    { value: IMPORT_MODE.move, label: "Move (delete torrent after import)" },
    { value: IMPORT_MODE.copy, label: "Copy (keep seeding)" },
  ];
  const reasonLabels: Record<string, string> = {
    [BLOCKLIST_REASON.failed]: "Download failed",
    [BLOCKLIST_REASON.stalled]: "Stalled",
    [BLOCKLIST_REASON.noImportableFiles]: "No importable files",
    [BLOCKLIST_REASON.manual]: "Manual",
  };
  const rootOptions = $derived(bookRoots.map((r) => ({ value: r.id, label: r.label || r.path })));

  async function load() {
    try {
      const [idx, clients, profs, bl, config] = await Promise.all([
        fetchIndexers(),
        fetchDownloadClients(),
        fetchAcquisitionProfiles(),
        // Secondary surface: a blocklist failure must not take down indexers/clients/profiles/config.
        fetchBlocklist().catch(() => [] as AcquisitionBlocklistEntry[]),
        fetchLibraryConfig(),
      ]);
      indexers = idx;
      downloadClients = clients;
      profiles = profs;
      blocklist = bl;
      bookRoots = config.roots.filter((r) => r.scanBooks);

      const setting = findSetting(config.settings, settingKeys.requestFulfillmentByKind);
      const parsed: Record<string, string> = {};
      for (const entry of valueAsStringList(setting?.value)) {
        const [kind, mode] = entry.split(":");
        if (kind && mode) parsed[kind] = mode;
      }
      routing = parsed;
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to load acquisition settings");
    } finally {
      loading = false;
    }
  }

  // ── Indexers ────────────────────────────────────────────────
  function newIndexer() {
    indexerForm = { id: null, kind: INDEXER_KIND.prowlarr, displayName: "Prowlarr", baseUrl: "", apiKey: null, enabled: true, priority: 25, categories: [7000, 8000] };
    indexerCategories = "7000,8000";
  }
  function editIndexer(item: IndexerConfigSummary) {
    indexerForm = { id: item.id, kind: item.kind, displayName: item.displayName, baseUrl: item.baseUrl, apiKey: null, enabled: item.enabled, priority: item.priority, categories: item.categories };
    indexerCategories = item.categories.join(",");
  }
  function parseCategories(text: string): number[] {
    return text.split(",").map((s) => Number(s.trim())).filter((n) => Number.isFinite(n) && n > 0);
  }
  async function testIndexer() {
    if (!indexerForm) return;
    busy = true;
    try {
      const res = await testIndexerConnection({ id: indexerForm.id, kind: indexerForm.kind, baseUrl: indexerForm.baseUrl, apiKey: indexerForm.apiKey });
      res.connected ? onMessage(res.message ?? "Connected") : onError(res.message ?? "Connection failed");
    } catch (err) {
      onError(err instanceof Error ? err.message : "Test failed");
    } finally {
      busy = false;
    }
  }
  async function saveIndexer() {
    if (!indexerForm) return;
    busy = true;
    try {
      await saveIndexerConfig({ ...indexerForm, categories: parseCategories(indexerCategories) });
      indexerForm = null;
      onMessage("Indexer saved");
      await load();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to save indexer");
    } finally {
      busy = false;
    }
  }
  async function removeIndexer(id: string) {
    busy = true;
    try {
      await deleteIndexerConfig(id);
      await load();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to delete indexer");
    } finally {
      busy = false;
    }
  }

  // ── Download clients ────────────────────────────────────────
  function newClient() {
    clientForm = { id: null, kind: DOWNLOAD_CLIENT_KIND.qBittorrent, displayName: "qBittorrent", baseUrl: "", username: null, password: null, category: "prismedia-books", enabled: true };
  }
  function editClient(item: DownloadClientSummary) {
    clientForm = { id: item.id, kind: item.kind, displayName: item.displayName, baseUrl: item.baseUrl, username: item.username, password: null, category: item.category, enabled: item.enabled };
  }
  async function testClient() {
    if (!clientForm) return;
    busy = true;
    try {
      const res = await testDownloadClientConnection({ id: clientForm.id, kind: clientForm.kind, baseUrl: clientForm.baseUrl, username: clientForm.username, password: clientForm.password });
      res.connected ? onMessage(res.message ?? "Connected") : onError(res.message ?? "Connection failed");
    } catch (err) {
      onError(err instanceof Error ? err.message : "Test failed");
    } finally {
      busy = false;
    }
  }
  async function saveClient() {
    if (!clientForm) return;
    busy = true;
    try {
      await saveDownloadClientConfig(clientForm);
      clientForm = null;
      onMessage("Download client saved");
      await load();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to save download client");
    } finally {
      busy = false;
    }
  }
  async function removeClient(id: string) {
    busy = true;
    try {
      await deleteDownloadClientConfig(id);
      await load();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to delete download client");
    } finally {
      busy = false;
    }
  }

  // ── Book profile ────────────────────────────────────────────
  function newProfile() {
    profileForm = {
      id: null, displayName: "Default Books", isDefault: profiles.length === 0,
      targetLibraryRootId: bookRoots[0]?.id ?? "", pathTemplate: DEFAULT_PATH_TEMPLATE,
      importMode: IMPORT_MODE.move, allowedFormats: [], language: null, minSeeders: 1,
      minSizeBytes: null, maxSizeBytes: null, requiredTerms: [], ignoredTerms: [], autoPick: false, autoRedownload: false,
    };
  }
  function editProfile(p: BookAcquisitionProfileView) {
    profileForm = {
      id: p.id, displayName: p.displayName, isDefault: p.isDefault, targetLibraryRootId: p.targetLibraryRootId,
      pathTemplate: p.pathTemplate, importMode: p.importMode, allowedFormats: p.allowedFormats, language: p.language,
      minSeeders: p.minSeeders, minSizeBytes: p.minSizeBytes, maxSizeBytes: p.maxSizeBytes,
      requiredTerms: p.requiredTerms, ignoredTerms: p.ignoredTerms, autoPick: p.autoPick, autoRedownload: p.autoRedownload,
    };
  }
  async function saveProfile() {
    if (!profileForm) return;
    busy = true;
    try {
      await saveAcquisitionProfile(profileForm);
      profileForm = null;
      onMessage("Profile saved");
      await load();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to save profile");
    } finally {
      busy = false;
    }
  }
  async function removeProfile(id: string) {
    busy = true;
    try {
      await deleteAcquisitionProfileConfig(id);
      await load();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to delete profile");
    } finally {
      busy = false;
    }
  }

  // ── Blocklist ───────────────────────────────────────────────
  async function removeBlocklistEntry(id: string) {
    busy = true;
    try {
      await deleteBlocklistEntry(id);
      onMessage("Removed from blocklist");
      // Only the blocklist changed — refetch just it rather than reloading the whole panel.
      blocklist = await fetchBlocklist();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to remove blocklist entry");
    } finally {
      busy = false;
    }
  }

  function modeFor(kind: string): string {
    return routing[kind] ?? FULFILLMENT_MODE.external;
  }

  async function setRouting(kind: string, mode: string) {
    if (busy) return;
    busy = true;
    try {
      const next = { ...routing, [kind]: mode };
      const entries = ROUTING_KINDS.map((k) => `${k.kind}:${next[k.kind] ?? FULFILLMENT_MODE.external}`);
      await updateSetting(settingKeys.requestFulfillmentByKind, entries);
      routing = next;
      onMessage("Fulfilment routing updated");
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to update routing");
    } finally {
      busy = false;
    }
  }

  onMount(load);
</script>

<Panel>
  <div class="space-y-6 p-4">
    <header class="flex items-center gap-2.5">
      <Boxes class="h-5 w-5 text-text-accent" />
      <div>
        <h2 class="text-base font-semibold text-text-primary">Acquisition</h2>
        <p class="text-[0.78rem] text-text-muted">
          Search indexers and download directly into your library (Prismedia-managed fulfilment).
        </p>
      </div>
    </header>

    {#if loading}
      <div class="flex items-center gap-2 text-sm text-text-muted"><Loader2 class="h-4 w-4 animate-spin" /> Loading…</div>
    {:else}
      <!-- Indexers -->
      <section class="space-y-2">
        <div class="flex items-center justify-between">
          <h3 class="text-kicker text-text-primary">Indexers</h3>
          {#if !indexerForm}
            <Button size="sm" variant="secondary" onclick={newIndexer} class="gap-1.5"><Plus class="h-3.5 w-3.5" /> Add</Button>
          {/if}
        </div>
        {#each indexers as item (item.id)}
          <div class="flex items-center justify-between rounded-sm border border-border-subtle bg-surface-1 px-3 py-2">
            <div class="flex items-center gap-2">
              <StatusLed status={item.enabled ? "active" : "idle"} />
              <span class="text-sm text-text-primary">{item.displayName}</span>
              <span class="text-xs text-text-muted">{item.baseUrl}</span>
              {#if item.hasApiKey}<Badge variant="default">key set</Badge>{/if}
            </div>
            <div class="flex items-center gap-1">
              <Button size="sm" variant="ghost" onclick={() => editIndexer(item)} disabled={busy}><Pencil class="h-3.5 w-3.5" /></Button>
              <Button size="sm" variant="ghost" onclick={() => removeIndexer(item.id)} disabled={busy}><Trash2 class="h-3.5 w-3.5" /></Button>
            </div>
          </div>
        {/each}
        {#if indexerForm}
          <div class="space-y-2 rounded-sm border border-border-accent bg-surface-1 p-3">
            <div class="grid gap-2 sm:grid-cols-2">
              <label class="space-y-1"><span class="text-label text-text-muted">Name</span>
                <TextInput size="sm" value={indexerForm.displayName} oninput={(e) => indexerForm && (indexerForm.displayName = e.currentTarget.value)} /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Base URL</span>
                <TextInput size="sm" value={indexerForm.baseUrl} oninput={(e) => indexerForm && (indexerForm.baseUrl = e.currentTarget.value)} placeholder="https://prowlarr.example.com" /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">API key</span>
                <TextInput size="sm" type="password" value={indexerForm.apiKey ?? ""} oninput={(e) => indexerForm && (indexerForm.apiKey = e.currentTarget.value)} placeholder={indexerForm.id ? "(unchanged)" : ""} /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Categories</span>
                <TextInput size="sm" value={indexerCategories} oninput={(e) => (indexerCategories = e.currentTarget.value)} placeholder="7000,8000" /></label>
            </div>
            <div class="flex items-center justify-between">
              <Button size="sm" variant="ghost" onclick={testIndexer} disabled={busy} class="gap-1.5"><PlugZap class="h-3.5 w-3.5" /> Test</Button>
              <div class="flex gap-1.5">
                <Button size="sm" variant="ghost" onclick={() => (indexerForm = null)} disabled={busy}>Cancel</Button>
                <Button size="sm" variant="primary" onclick={saveIndexer} disabled={busy || !indexerForm.displayName || !indexerForm.baseUrl}>Save</Button>
              </div>
            </div>
          </div>
        {/if}
      </section>

      <!-- Download clients -->
      <section class="space-y-2">
        <div class="flex items-center justify-between">
          <h3 class="text-kicker text-text-primary">Download clients</h3>
          {#if !clientForm}
            <Button size="sm" variant="secondary" onclick={newClient} class="gap-1.5"><Plus class="h-3.5 w-3.5" /> Add</Button>
          {/if}
        </div>
        {#each downloadClients as item (item.id)}
          <div class="flex items-center justify-between rounded-sm border border-border-subtle bg-surface-1 px-3 py-2">
            <div class="flex items-center gap-2">
              <StatusLed status={item.enabled ? "active" : "idle"} />
              <span class="text-sm text-text-primary">{item.displayName}</span>
              <span class="text-xs text-text-muted">{item.baseUrl}</span>
              <Badge variant="default">{item.category}</Badge>
            </div>
            <div class="flex items-center gap-1">
              <Button size="sm" variant="ghost" onclick={() => editClient(item)} disabled={busy}><Pencil class="h-3.5 w-3.5" /></Button>
              <Button size="sm" variant="ghost" onclick={() => removeClient(item.id)} disabled={busy}><Trash2 class="h-3.5 w-3.5" /></Button>
            </div>
          </div>
        {/each}
        {#if clientForm}
          <div class="space-y-2 rounded-sm border border-border-accent bg-surface-1 p-3">
            <div class="grid gap-2 sm:grid-cols-2">
              <label class="space-y-1"><span class="text-label text-text-muted">Name</span>
                <TextInput size="sm" value={clientForm.displayName} oninput={(e) => clientForm && (clientForm.displayName = e.currentTarget.value)} /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Base URL</span>
                <TextInput size="sm" value={clientForm.baseUrl} oninput={(e) => clientForm && (clientForm.baseUrl = e.currentTarget.value)} placeholder="http://localhost:8080" /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Username</span>
                <TextInput size="sm" value={clientForm.username ?? ""} oninput={(e) => clientForm && (clientForm.username = e.currentTarget.value)} /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Password</span>
                <TextInput size="sm" type="password" value={clientForm.password ?? ""} oninput={(e) => clientForm && (clientForm.password = e.currentTarget.value)} placeholder={clientForm.id ? "(unchanged)" : ""} /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Category / label</span>
                <TextInput size="sm" value={clientForm.category} oninput={(e) => clientForm && (clientForm.category = e.currentTarget.value)} /></label>
            </div>
            <div class="flex items-center justify-between">
              <Button size="sm" variant="ghost" onclick={testClient} disabled={busy} class="gap-1.5"><PlugZap class="h-3.5 w-3.5" /> Test</Button>
              <div class="flex gap-1.5">
                <Button size="sm" variant="ghost" onclick={() => (clientForm = null)} disabled={busy}>Cancel</Button>
                <Button size="sm" variant="primary" onclick={saveClient} disabled={busy || !clientForm.displayName || !clientForm.baseUrl || !clientForm.category}>Save</Button>
              </div>
            </div>
          </div>
        {/if}
      </section>

      <!-- Book profile -->
      <section class="space-y-2">
        <div class="flex items-center justify-between">
          <h3 class="text-kicker text-text-primary">Book profile</h3>
          {#if !profileForm}
            <Button size="sm" variant="secondary" onclick={newProfile} disabled={bookRoots.length === 0} class="gap-1.5"><Plus class="h-3.5 w-3.5" /> Add</Button>
          {/if}
        </div>
        {#if bookRoots.length === 0}
          <p class="text-[0.78rem] text-text-muted">Add a library root with book scanning enabled to create a profile.</p>
        {/if}
        {#each profiles as p (p.id)}
          <div class="flex items-center justify-between rounded-sm border border-border-subtle bg-surface-1 px-3 py-2">
            <div class="flex items-center gap-2">
              <span class="text-sm text-text-primary">{p.displayName}</span>
              {#if p.isDefault}<Badge variant="accent">default</Badge>{/if}
              <span class="text-xs text-text-muted">→ {bookRoots.find((r) => r.id === p.targetLibraryRootId)?.label ?? "root"}</span>
            </div>
            <div class="flex items-center gap-1">
              <Button size="sm" variant="ghost" onclick={() => editProfile(p)} disabled={busy}><Pencil class="h-3.5 w-3.5" /></Button>
              <Button size="sm" variant="ghost" onclick={() => removeProfile(p.id)} disabled={busy}><Trash2 class="h-3.5 w-3.5" /></Button>
            </div>
          </div>
        {/each}
        {#if profileForm}
          <div class="space-y-2 rounded-sm border border-border-accent bg-surface-1 p-3">
            <div class="grid gap-2 sm:grid-cols-2">
              <label class="space-y-1"><span class="text-label text-text-muted">Name</span>
                <TextInput size="sm" value={profileForm.displayName} oninput={(e) => profileForm && (profileForm.displayName = e.currentTarget.value)} /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Target library root</span>
                <Select size="sm" value={profileForm.targetLibraryRootId} options={rootOptions} onchange={(v) => profileForm && (profileForm.targetLibraryRootId = v)} /></label>
              <label class="space-y-1 sm:col-span-2"><span class="text-label text-text-muted">Path template</span>
                <TextInput size="sm" value={profileForm.pathTemplate} oninput={(e) => profileForm && (profileForm.pathTemplate = e.currentTarget.value)} /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Import mode</span>
                <Select size="sm" value={profileForm.importMode} options={importModeOptions} onchange={(v) => profileForm && (profileForm.importMode = v as typeof profileForm.importMode)} /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Min seeders</span>
                <TextInput size="sm" value={String(profileForm.minSeeders)} oninput={(e) => profileForm && (profileForm.minSeeders = Number(e.currentTarget.value) || 0)} /></label>
            </div>
            <label class="flex items-center gap-2"><Checkbox checked={profileForm.isDefault} onchange={(e) => profileForm && (profileForm.isDefault = e.currentTarget.checked)} /><span class="text-sm text-text-secondary">Default profile</span></label>
            <label class="flex items-start gap-2"><Checkbox checked={profileForm.autoPick} onchange={(e) => profileForm && (profileForm.autoPick = e.currentTarget.checked)} /><span class="text-sm text-text-secondary">Auto-grab<span class="block text-[0.72rem] text-text-muted">Download the best acceptable release automatically instead of waiting for manual review.</span></span></label>
            <label class="flex items-start gap-2"><Checkbox checked={profileForm.autoRedownload} onchange={(e) => profileForm && (profileForm.autoRedownload = e.currentTarget.checked)} /><span class="text-sm text-text-secondary">Auto-redownload on failure<span class="block text-[0.72rem] text-text-muted">When a download fails, blocklist that release and automatically grab the next-best candidate.</span></span></label>
            <div class="flex justify-end gap-1.5">
              <Button size="sm" variant="ghost" onclick={() => (profileForm = null)} disabled={busy}>Cancel</Button>
              <Button size="sm" variant="primary" onclick={saveProfile} disabled={busy || !profileForm.displayName || !profileForm.targetLibraryRootId}>Save</Button>
            </div>
          </div>
        {/if}
      </section>

      <!-- Blocklist -->
      <section class="space-y-2">
        <h3 class="text-kicker text-text-primary">Blocklist</h3>
        <p class="text-[0.72rem] text-text-muted">
          Releases a failed download blocklisted so they are never grabbed again. Remove an entry to allow that release to be acquired once more.
        </p>
        {#if blocklist.length === 0}
          <p class="text-[0.78rem] text-text-muted">No blocklisted releases.</p>
        {/if}
        {#each blocklist as entry (entry.id)}
          <div class="flex items-center justify-between rounded-sm border border-border-subtle bg-surface-1 px-3 py-2">
            <div class="flex min-w-0 items-center gap-2">
              <Badge variant="default">{reasonLabels[entry.reason] ?? entry.reason}</Badge>
              <span class="truncate text-sm text-text-primary">{entry.title ?? "Unknown release"}</span>
              {#if entry.indexerName}<span class="shrink-0 text-xs text-text-muted">{entry.indexerName}</span>{/if}
            </div>
            <Button size="sm" variant="ghost" onclick={() => removeBlocklistEntry(entry.id)} disabled={busy}><Trash2 class="h-3.5 w-3.5" /></Button>
          </div>
        {/each}
      </section>

      <!-- Fulfilment routing -->
      <section class="space-y-2">
        <h3 class="text-kicker text-text-primary">Fulfilment routing</h3>
        <p class="text-[0.72rem] text-text-muted">
          How each kind of request is fulfilled. Prismedia downloads directly; External hands off to a configured app (Radarr/Sonarr/Lidarr).
        </p>
        {#each ROUTING_KINDS as r (r.kind)}
          <div class="flex items-center justify-between rounded-sm border border-border-subtle bg-surface-1 px-3 py-2">
            <span class="text-sm text-text-primary">{r.label}</span>
            <div class="flex gap-1">
              {#each [{ m: FULFILLMENT_MODE.prismedia, l: "Prismedia" }, { m: FULFILLMENT_MODE.external, l: "External" }] as opt (opt.m)}
                <button
                  type="button"
                  onclick={() => setRouting(r.kind, opt.m)}
                  disabled={busy}
                  class={cn(
                    "rounded-xs border px-2.5 py-1 text-[0.72rem] font-medium transition-all duration-fast",
                    modeFor(r.kind) === opt.m
                      ? "bg-accent-950/30 border-border-accent text-text-accent"
                      : "bg-surface-2 border-border-subtle text-text-muted hover:text-text-primary",
                  )}
                >
                  {opt.l}
                </button>
              {/each}
            </div>
          </div>
        {/each}
      </section>
    {/if}
  </div>
</Panel>
