<script lang="ts">
  import { onMount } from "svelte";
  import { Boxes, Loader2, Pencil, PlugZap, Plus, Trash2 } from "@lucide/svelte";
  import { Badge, Button, Checkbox, Panel, Select, StatusLed, TextInput } from "@prismedia/ui-svelte";
  import { INDEXER_KIND, DOWNLOAD_CLIENT_KIND, IMPORT_MODE, BLOCKLIST_REASON, BOOK_SOURCE_TIER, BOOK_FORMAT_TIER, ENTITY_KIND } from "$lib/api/generated/codes";
  import { cn } from "@prismedia/ui-svelte";
  import type {
    AcquisitionBlocklistEntry,
    BookAcquisitionProfileSaveRequest,
    BookAcquisitionProfileView,
    DownloadClientSaveRequest,
    DownloadClientSummary,
    IndexerConfigSaveRequest,
    IndexerConfigSummary,
    WeightedTerm,
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
  let allRoots = $state<LibraryRoot[]>([]);
  let loading = $state(true);
  let busy = $state(false);

  // Inline edit forms (null = closed).
  let indexerForm = $state<IndexerConfigSaveRequest | null>(null);
  let indexerCategories = $state("7000,8000");
  let clientForm = $state<DownloadClientSaveRequest | null>(null);
  let profileForm = $state<BookAcquisitionProfileSaveRequest | null>(null);
  let profileTerms = $state({ preferred: "", required: "", ignored: "", weighted: "", languages: "" });

  const importModeOptions = [
    { value: IMPORT_MODE.move, label: "Move (delete torrent after import)" },
    { value: IMPORT_MODE.copy, label: "Copy (keep seeding)" },
  ];
  // Cutoff tiers: the quality at which the upgrade loop stops searching. Ordered worst→best to match the ranks.
  const cutoffSourceOptions = [
    { value: BOOK_SOURCE_TIER.unknown, label: "Unknown (any source)" },
    { value: BOOK_SOURCE_TIER.web, label: "Web" },
    { value: BOOK_SOURCE_TIER.retail, label: "Retail" },
  ];
  const cutoffFormatOptions = [
    { value: BOOK_FORMAT_TIER.unknown, label: "Unknown (any format)" },
    { value: BOOK_FORMAT_TIER.fixed, label: "PDF" },
    { value: BOOK_FORMAT_TIER.reflowable, label: "EPUB" },
    { value: BOOK_FORMAT_TIER.archive, label: "Comic archive (CBZ)" },
  ];
  const reasonLabels: Record<string, string> = {
    [BLOCKLIST_REASON.failed]: "Download failed",
    [BLOCKLIST_REASON.stalled]: "Stalled",
    [BLOCKLIST_REASON.noImportableFiles]: "No importable files",
    [BLOCKLIST_REASON.manual]: "Manual",
  };
  // Profiles are kind-scoped: each kind offers only the libraries that can hold its media.
  const profileKindOptions = [
    { value: ENTITY_KIND.book, label: "Books" },
    { value: ENTITY_KIND.movie, label: "Movies" },
    { value: ENTITY_KIND.audioLibrary, label: "Music (albums)" },
  ];
  const profileKindLabels: Record<string, string> = Object.fromEntries(
    profileKindOptions.map((option) => [option.value, option.label]),
  );
  function rootsForKind(kind: string): LibraryRoot[] {
    return allRoots.filter((r) =>
      kind === ENTITY_KIND.movie ? r.scanVideos : kind === ENTITY_KIND.audioLibrary ? r.scanAudio : r.scanBooks);
  }
  const bookRoots = $derived(rootsForKind(ENTITY_KIND.book));
  const formRoots = $derived(profileForm ? rootsForKind(profileForm.kind) : []);
  const rootOptions = $derived(formRoots.map((r) => ({ value: r.id, label: r.label || r.path })));
  const formIsBookKind = $derived(profileForm?.kind === ENTITY_KIND.book);

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
      allRoots = config.roots;
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

  // ── Acquisition profiles (kind-scoped) ──────────────────────
  function newProfile() {
    profileForm = {
      id: null, displayName: "Default Books", isDefault: !profiles.some((p) => p.kind === ENTITY_KIND.book),
      kind: ENTITY_KIND.book,
      targetLibraryRootId: bookRoots[0]?.id ?? "", pathTemplate: DEFAULT_PATH_TEMPLATE,
      importMode: IMPORT_MODE.move, allowedFormats: [], preferredLanguages: ["English"], minSeeders: 1,
      minSizeBytes: null, maxSizeBytes: null, requiredTerms: [], ignoredTerms: [], preferredTerms: [], weightedTerms: [], autoPick: false, autoRedownload: false,
      upgradeUntilCutoff: false, cutoffSourceTier: BOOK_SOURCE_TIER.unknown, cutoffFormatTier: BOOK_FORMAT_TIER.unknown,
    };
    profileTerms = { preferred: "", required: "", ignored: "", weighted: "", languages: "English" };
  }
  /** Switching the form's kind re-targets the root (each kind offers different libraries) and refreshes the suggested name. */
  function setProfileKind(kind: string) {
    if (!profileForm) return;
    profileForm.kind = kind as typeof profileForm.kind;
    const suitable = rootsForKind(kind);
    if (!suitable.some((r) => r.id === profileForm?.targetLibraryRootId)) {
      profileForm.targetLibraryRootId = suitable[0]?.id ?? "";
    }
    if (!profileForm.id && /^Default /.test(profileForm.displayName)) {
      profileForm.displayName = `Default ${profileKindLabels[kind] ?? kind}`;
      profileForm.isDefault = !profiles.some((p) => p.kind === kind);
    }
  }
  function editProfile(p: BookAcquisitionProfileView) {
    profileForm = {
      id: p.id, displayName: p.displayName, isDefault: p.isDefault, kind: p.kind, targetLibraryRootId: p.targetLibraryRootId,
      pathTemplate: p.pathTemplate, importMode: p.importMode, allowedFormats: p.allowedFormats, preferredLanguages: p.preferredLanguages,
      minSeeders: p.minSeeders, minSizeBytes: p.minSizeBytes, maxSizeBytes: p.maxSizeBytes,
      requiredTerms: p.requiredTerms, ignoredTerms: p.ignoredTerms, preferredTerms: p.preferredTerms, weightedTerms: p.weightedTerms,
      autoPick: p.autoPick, autoRedownload: p.autoRedownload,
      upgradeUntilCutoff: p.upgradeUntilCutoff, cutoffSourceTier: p.cutoffSourceTier, cutoffFormatTier: p.cutoffFormatTier,
    };
    profileTerms = {
      preferred: p.preferredTerms.join(", "),
      required: p.requiredTerms.join(", "),
      ignored: p.ignoredTerms.join(", "),
      weighted: p.weightedTerms.map((t) => `${t.term}: ${t.weight}`).join(", "),
      languages: p.preferredLanguages.join(", "),
    };
  }
  // Comma/newline-separated term lists are edited as text and parsed on save.
  function parseTerms(text: string): string[] {
    return text.split(/[,\n]/).map((term) => term.trim()).filter((term) => term.length > 0);
  }
  // Weighted terms are edited as "term: weight" entries; entries without a numeric weight are dropped.
  function parseWeightedTerms(text: string): WeightedTerm[] {
    return text
      .split(/[,\n]/)
      .map((entry): WeightedTerm | null => {
        const match = /^(.*?)[:=]\s*(-?\d+)\s*$/.exec(entry.trim());
        if (!match) return null;
        const term = match[1].trim();
        const weight = Number(match[2]);
        return term && weight !== 0 ? { term, weight } : null;
      })
      .filter((entry): entry is WeightedTerm => entry !== null);
  }
  async function saveProfile() {
    if (!profileForm) return;
    profileForm.preferredTerms = parseTerms(profileTerms.preferred);
    profileForm.requiredTerms = parseTerms(profileTerms.required);
    profileForm.ignoredTerms = parseTerms(profileTerms.ignored);
    profileForm.weightedTerms = parseWeightedTerms(profileTerms.weighted);
    profileForm.preferredLanguages = parseTerms(profileTerms.languages);
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

      <!-- Acquisition profiles (per media kind) -->
      <section class="space-y-2">
        <div class="flex items-center justify-between">
          <h3 class="text-kicker text-text-primary">Profiles</h3>
          {#if !profileForm}
            <Button size="sm" variant="secondary" onclick={newProfile} disabled={allRoots.length === 0} class="gap-1.5"><Plus class="h-3.5 w-3.5" /> Add</Button>
          {/if}
        </div>
        {#if allRoots.length === 0}
          <p class="text-[0.78rem] text-text-muted">Add a library root first to create an acquisition profile.</p>
        {/if}
        {#each profiles as p (p.id)}
          <div class="flex items-center justify-between rounded-sm border border-border-subtle bg-surface-1 px-3 py-2">
            <div class="flex items-center gap-2">
              <Badge variant="default">{profileKindLabels[p.kind] ?? p.kind}</Badge>
              <span class="text-sm text-text-primary">{p.displayName}</span>
              {#if p.isDefault}<Badge variant="accent">default</Badge>{/if}
              <span class="text-xs text-text-muted">→ {allRoots.find((r) => r.id === p.targetLibraryRootId)?.label ?? "root"}</span>
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
              <label class="space-y-1"><span class="text-label text-text-muted">Media kind</span>
                <Select size="sm" value={profileForm.kind} options={profileKindOptions} onchange={setProfileKind} /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Name</span>
                <TextInput size="sm" value={profileForm.displayName} oninput={(e) => profileForm && (profileForm.displayName = e.currentTarget.value)} /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Target library root</span>
                <Select size="sm" value={profileForm.targetLibraryRootId} options={rootOptions} onchange={(v) => profileForm && (profileForm.targetLibraryRootId = v)} /></label>
              {#if formIsBookKind}
                <label class="space-y-1 sm:col-span-2"><span class="text-label text-text-muted">Path template</span>
                  <TextInput size="sm" value={profileForm.pathTemplate} oninput={(e) => profileForm && (profileForm.pathTemplate = e.currentTarget.value)} /></label>
              {:else}
                <p class="sm:col-span-2 text-[0.72rem] leading-relaxed text-text-muted">
                  Placement is fixed to match library scanning: movies land as <span class="font-mono">Title (Year)/Title (Year).ext</span>,
                  albums as <span class="font-mono">Artist/Album/</span>.
                </p>
              {/if}
              <label class="space-y-1"><span class="text-label text-text-muted">Import mode</span>
                <Select size="sm" value={profileForm.importMode} options={importModeOptions} onchange={(v) => profileForm && (profileForm.importMode = v as typeof profileForm.importMode)} /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Min seeders</span>
                <TextInput size="sm" value={String(profileForm.minSeeders)} oninput={(e) => profileForm && (profileForm.minSeeders = Number(e.currentTarget.value) || 0)} /></label>
            </div>
            <div class="grid gap-2">
              <label class="space-y-1"><span class="text-label text-text-muted">Preferred terms<span class="ml-1 text-text-muted">— comma-separated; matches rank a release higher (e.g. retail, epub)</span></span>
                <TextInput size="sm" value={profileTerms.preferred} oninput={(e) => (profileTerms.preferred = e.currentTarget.value)} placeholder="retail, epub" /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Required terms<span class="ml-1 text-text-muted">— a release must contain all of these</span></span>
                <TextInput size="sm" value={profileTerms.required} oninput={(e) => (profileTerms.required = e.currentTarget.value)} /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Ignored terms<span class="ml-1 text-text-muted">— a release containing any of these is rejected</span></span>
                <TextInput size="sm" value={profileTerms.ignored} oninput={(e) => (profileTerms.ignored = e.currentTarget.value)} placeholder="scan, retail rip" /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Weighted terms<span class="ml-1 text-text-muted">— "term: weight" entries; a match adds its weight to the ranking (100 equals one preferred term, negatives push down)</span></span>
                <TextInput size="sm" value={profileTerms.weighted} oninput={(e) => (profileTerms.weighted = e.currentTarget.value)} placeholder="remux: 150, x265: 50, upscale: -200" /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Preferred languages<span class="ml-1 text-text-muted">— in order of preference; releases tagged only with other languages are skipped, untagged ones count as the first entry</span></span>
                <TextInput size="sm" value={profileTerms.languages} oninput={(e) => (profileTerms.languages = e.currentTarget.value)} placeholder="English" /></label>
            </div>
            <label class="flex items-center gap-2"><Checkbox checked={profileForm.isDefault} onchange={(e) => profileForm && (profileForm.isDefault = e.currentTarget.checked)} /><span class="text-sm text-text-secondary">Default profile</span></label>
            <label class="flex items-start gap-2"><Checkbox checked={profileForm.autoPick} onchange={(e) => profileForm && (profileForm.autoPick = e.currentTarget.checked)} /><span class="text-sm text-text-secondary">Auto-grab<span class="block text-[0.72rem] text-text-muted">Download the best acceptable release automatically instead of waiting for manual review.</span></span></label>
            <label class="flex items-start gap-2"><Checkbox checked={profileForm.autoRedownload} onchange={(e) => profileForm && (profileForm.autoRedownload = e.currentTarget.checked)} /><span class="text-sm text-text-secondary">Auto-redownload on failure<span class="block text-[0.72rem] text-text-muted">When a download fails, blocklist that release and automatically grab the next-best candidate.</span></span></label>
            {#if formIsBookKind}
            <label class="flex items-start gap-2"><Checkbox checked={profileForm.upgradeUntilCutoff} onchange={(e) => profileForm && (profileForm.upgradeUntilCutoff = e.currentTarget.checked)} /><span class="text-sm text-text-secondary">Upgrade until cutoff<span class="block text-[0.72rem] text-text-muted">After a book is imported, keep searching for a higher-quality release and replace the file, until it reaches the cutoff below.</span></span></label>
            {/if}
            {#if formIsBookKind && profileForm.upgradeUntilCutoff}
              <div class="grid gap-2 sm:grid-cols-2 pl-6">
                <label class="space-y-1"><span class="text-label text-text-muted">Cutoff source</span>
                  <Select size="sm" value={profileForm.cutoffSourceTier} options={cutoffSourceOptions} onchange={(v) => profileForm && (profileForm.cutoffSourceTier = v as typeof profileForm.cutoffSourceTier)} /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">Cutoff format</span>
                  <Select size="sm" value={profileForm.cutoffFormatTier} options={cutoffFormatOptions} onchange={(v) => profileForm && (profileForm.cutoffFormatTier = v as typeof profileForm.cutoffFormatTier)} /></label>
              </div>
            {/if}
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
    {/if}
  </div>
</Panel>
