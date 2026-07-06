<script lang="ts">
  import { onMount } from "svelte";
  import { Boxes, CircleAlert, CircleCheck, Loader2, Pencil, PlugZap, Plus, Trash2 } from "@lucide/svelte";
  import { Badge, Button, Checkbox, Panel, Select, StatusLed, TextInput } from "@prismedia/ui-svelte";
  import { INDEXER_KIND, DOWNLOAD_CLIENT_KIND, IMPORT_MODE, BLOCKLIST_REASON, BOOK_SOURCE_TIER, BOOK_FORMAT_TIER, ENTITY_KIND, VIDEO_QUALITY, AUDIO_QUALITY, CUSTOM_FORMAT_CONDITION_TYPE } from "$lib/api/generated/codes";
  import { cn } from "@prismedia/ui-svelte";
  import type {
    AcquisitionBlocklistEntry,
    BookAcquisitionProfileSaveRequest,
    BookAcquisitionProfileView,
    CustomFormatConditionView,
    CustomFormatSaveRequest,
    CustomFormatView,
    DownloadClientSaveRequest,
    DownloadClientSummary,
    IndexerConfigSaveRequest,
    IndexerConfigSummary,
    RemotePathMappingView,
    WeightedTerm,
  } from "$lib/api/generated/model";
  import {
    deleteAcquisitionProfileConfig,
    deleteBlocklistEntry,
    deleteCustomFormat,
    deleteDownloadClientConfig,
    deleteIndexerConfig,
    fetchAcquisitionProfiles,
    fetchBlocklist,
    fetchCustomFormats,
    fetchDownloadClients,
    fetchIndexers,
    fetchRemotePathMappings,
    saveRemotePathMapping,
    deleteRemotePathMapping,
    saveAcquisitionProfile,
    saveCustomFormat,
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
  // Per-kind naming templates, mirroring the backend MediaNamingTemplates defaults. Books own their own
  // template; movies/TV/music each control names within a structure the scan binding depends on.
  const NAMING_DEFAULTS: Record<string, string> = {
    [ENTITY_KIND.book]: DEFAULT_PATH_TEMPLATE,
    [ENTITY_KIND.movie]: "{Title} ({Year})/{Title} ({Year}).{ext}",
    [ENTITY_KIND.videoSeries]: "{Series}/Season {Season:00}/{Series} - S{Season:00}E{Episode:00}.{ext}",
    [ENTITY_KIND.audioLibrary]: "{Artist}/{Album}",
  };
  // A short per-kind token hint shown under the naming template input.
  const NAMING_HINTS: Record<string, string> = {
    [ENTITY_KIND.book]: "{Author} {Title} {Year} {ext} — folder/file layout for the book payload",
    [ENTITY_KIND.movie]: "{Title} {Year} {Quality} {ext} — 2 segments: folder/file",
    [ENTITY_KIND.videoSeries]: "{Series} {Season} {Season:00} {Episode:00} {Quality} {ext} — 3 segments: series/season/episode",
    [ENTITY_KIND.audioLibrary]: "{Artist} {Album} {Year} — 2 segments: artist/album folder (track files keep their release names)",
  };
  function namingDefaultFor(kind: string): string {
    return NAMING_DEFAULTS[kind] ?? DEFAULT_PATH_TEMPLATE;
  }
  function namingHintFor(kind: string): string {
    return NAMING_HINTS[kind] ?? "";
  }

  let indexers = $state<IndexerConfigSummary[]>([]);
  let downloadClients = $state<DownloadClientSummary[]>([]);
  let profiles = $state<BookAcquisitionProfileView[]>([]);
  let customFormats = $state<CustomFormatView[]>([]);
  let blocklist = $state<AcquisitionBlocklistEntry[]>([]);
  let allRoots = $state<LibraryRoot[]>([]);
  let loading = $state(true);
  let busy = $state(false);

  // Inline edit forms (null = closed).
  let indexerForm = $state<IndexerConfigSaveRequest | null>(null);
  let indexerCategories = $state("7000,8000");
  let clientForm = $state<DownloadClientSaveRequest | null>(null);
  // Inline connection-test result, shown in the form next to the Test button (a top-of-page banner is
  // off-screen while editing a section deep in the list). null = not tested since the form opened.
  type TestResult = { state: "testing" | "ok" | "fail"; message: string };
  let indexerTest = $state<TestResult | null>(null);
  let clientTest = $state<TestResult | null>(null);
  let profileForm = $state<BookAcquisitionProfileSaveRequest | null>(null);
  let profileTerms = $state({ preferred: "", required: "", ignored: "", weighted: "", languages: "" });

  // The quality ladders per profile kind, worst → best, for the allowed-set picker and cutoff select.
  const videoQualityLadder = Object.values(VIDEO_QUALITY).filter((code) => code !== VIDEO_QUALITY.unknown);
  const audioQualityLadder = Object.values(AUDIO_QUALITY).filter((code) => code !== AUDIO_QUALITY.unknown);
  function qualityLadderFor(kind: string): string[] {
    if (kind === ENTITY_KIND.movie || kind === ENTITY_KIND.videoSeries) return videoQualityLadder;
    if (kind === ENTITY_KIND.audioLibrary) return audioQualityLadder;
    return [];
  }
  function toggleAllowedQuality(code: string) {
    if (!profileForm) return;
    const current = profileForm.allowedQualities ?? [];
    profileForm.allowedQualities = current.includes(code) ? current.filter((c) => c !== code) : [...current, code];
  }

  const importModeOptions = [
    { value: IMPORT_MODE.move, label: "Move (delete torrent after import)" },
    { value: IMPORT_MODE.hardlink, label: "Hardlink (instant, keeps seeding)" },
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
    { value: ENTITY_KIND.videoSeries, label: "TV (series)" },
    { value: ENTITY_KIND.audioLibrary, label: "Music (albums)" },
  ];
  const profileKindLabels: Record<string, string> = Object.fromEntries(
    profileKindOptions.map((option) => [option.value, option.label]),
  );
  function rootsForKind(kind: string): LibraryRoot[] {
    return allRoots.filter((r) =>
      kind === ENTITY_KIND.movie || kind === ENTITY_KIND.videoSeries
        ? r.scanVideos
        : kind === ENTITY_KIND.audioLibrary
          ? r.scanAudio
          : r.scanBooks);
  }
  const bookRoots = $derived(rootsForKind(ENTITY_KIND.book));
  const formRoots = $derived(profileForm ? rootsForKind(profileForm.kind) : []);
  const rootOptions = $derived(formRoots.map((r) => ({ value: r.id, label: r.label || r.path })));
  const formIsBookKind = $derived(profileForm?.kind === ENTITY_KIND.book);
  // Custom formats matching the profile form's current kind — the scorable set for this profile.
  const formatsForKind = $derived.by(() => {
    const kind = profileForm?.kind;
    return kind ? customFormats.filter((f) => f.kind === kind) : [];
  });
  /** Sets (or clears, when blank) the per-format score on the open profile form. */
  function setFormatScore(id: string, raw: string) {
    if (!profileForm) return;
    profileForm.formatScores ??= {};
    const next = { ...profileForm.formatScores };
    if (raw.trim() === "") {
      delete next[id];
    } else {
      next[id] = Number(raw) || 0;
    }
    profileForm.formatScores = next;
  }

  async function load() {
    try {
      const [idx, clients, profs, bl, config, mappings, formats] = await Promise.all([
        fetchIndexers(),
        fetchDownloadClients(),
        fetchAcquisitionProfiles(),
        // Secondary surface: a blocklist failure must not take down indexers/clients/profiles/config.
        fetchBlocklist().catch(() => [] as AcquisitionBlocklistEntry[]),
        fetchLibraryConfig(),
        fetchRemotePathMappings().catch(() => [] as RemotePathMappingView[]),
        fetchCustomFormats().catch(() => [] as CustomFormatView[]),
      ]);
      indexers = idx;
      downloadClients = clients;
      profiles = profs;
      pathMappings = mappings;
      blocklist = bl;
      allRoots = config.roots;
      customFormats = formats;
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to load acquisition settings");
    } finally {
      loading = false;
    }
  }

  // ── Indexers ────────────────────────────────────────────────
  const indexerKindOptions = [
    { value: INDEXER_KIND.prowlarr, label: "Prowlarr (aggregator)" },
    { value: INDEXER_KIND.torznab, label: "Torznab (torrent indexer / Jackett)" },
    { value: INDEXER_KIND.newznab, label: "Newznab (usenet indexer)" },
  ];
  const indexerKindDefaults: Record<string, string> = {
    [INDEXER_KIND.prowlarr]: "Prowlarr",
    [INDEXER_KIND.torznab]: "Torznab indexer",
    [INDEXER_KIND.newznab]: "Newznab indexer",
  };
  function indexerKindLabel(kind: string): string {
    return indexerKindOptions.find((option) => option.value === kind)?.label.split(" (")[0] ?? kind;
  }
  function setIndexerKind(kind: string) {
    if (!indexerForm) return;
    const untouched = indexerForm.displayName === indexerKindDefaults[indexerForm.kind];
    indexerForm.kind = kind as typeof indexerForm.kind;
    if (untouched) indexerForm.displayName = indexerKindDefaults[kind] ?? indexerForm.displayName;
  }
  function newIndexer() {
    indexerForm = { id: null, kind: INDEXER_KIND.prowlarr, displayName: "Prowlarr", baseUrl: "", apiKey: null, enabled: true, priority: 25, categories: [7000, 8000], queryLimitPerHour: null, seedRatio: null, seedTimeMinutes: null };
    indexerCategories = "7000,8000";
    indexerTest = null;
  }
  function editIndexer(item: IndexerConfigSummary) {
    indexerForm = { id: item.id, kind: item.kind, displayName: item.displayName, baseUrl: item.baseUrl, apiKey: null, enabled: item.enabled, priority: item.priority, categories: item.categories, queryLimitPerHour: item.queryLimitPerHour ?? null, seedRatio: item.seedRatio ?? null, seedTimeMinutes: item.seedTimeMinutes ?? null };
    indexerCategories = item.categories.join(",");
    indexerTest = null;
  }
  function parseCategories(text: string): number[] {
    return text.split(",").map((s) => Number(s.trim())).filter((n) => Number.isFinite(n) && n > 0);
  }
  async function testIndexer() {
    if (!indexerForm) return;
    indexerTest = { state: "testing", message: "Testing connection…" };
    try {
      const res = await testIndexerConnection({ id: indexerForm.id, kind: indexerForm.kind, baseUrl: indexerForm.baseUrl, apiKey: indexerForm.apiKey });
      indexerTest = res.connected
        ? { state: "ok", message: res.message ?? "Connected." }
        : { state: "fail", message: res.message ?? "Connection failed." };
    } catch (err) {
      indexerTest = { state: "fail", message: err instanceof Error ? err.message : "Test failed." };
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
  const clientKindOptions = [
    { value: DOWNLOAD_CLIENT_KIND.qBittorrent, label: "qBittorrent (torrent)" },
    { value: DOWNLOAD_CLIENT_KIND.transmission, label: "Transmission (torrent)" },
    { value: DOWNLOAD_CLIENT_KIND.sabnzbd, label: "SABnzbd (usenet)" },
  ];
  const clientKindDefaults: Record<string, string> = {
    [DOWNLOAD_CLIENT_KIND.qBittorrent]: "qBittorrent",
    [DOWNLOAD_CLIENT_KIND.transmission]: "Transmission",
    [DOWNLOAD_CLIENT_KIND.sabnzbd]: "SABnzbd",
  };
  function clientKindLabel(kind: string): string {
    return clientKindDefaults[kind] ?? kind;
  }
  function newClient() {
    clientForm = { id: null, kind: DOWNLOAD_CLIENT_KIND.qBittorrent, displayName: "qBittorrent", baseUrl: "", username: null, password: null, apiKey: null, category: "prismedia-books", enabled: true, priority: 25, seedRatio: null, seedTimeMinutes: null };
    clientTest = null;
  }
  function setClientKind(kind: string) {
    if (!clientForm) return;
    // A default display name tracks the kind switch; a user-edited one is left alone.
    const untouched = clientForm.displayName === clientKindDefaults[clientForm.kind];
    clientForm.kind = kind as DownloadClientSaveRequest["kind"];
    if (untouched) clientForm.displayName = clientKindDefaults[kind] ?? clientForm.displayName;
  }
  function editClient(item: DownloadClientSummary) {
    clientForm = { id: item.id, kind: item.kind, displayName: item.displayName, baseUrl: item.baseUrl, username: item.username, password: null, apiKey: null, category: item.category, enabled: item.enabled, priority: item.priority ?? 25, seedRatio: item.seedRatio ?? null, seedTimeMinutes: item.seedTimeMinutes ?? null };
    clientTest = null;
  }
  async function testClient() {
    if (!clientForm) return;
    clientTest = { state: "testing", message: "Testing connection…" };
    try {
      const res = await testDownloadClientConnection({ id: clientForm.id, kind: clientForm.kind, baseUrl: clientForm.baseUrl, username: clientForm.username, password: clientForm.password, apiKey: clientForm.apiKey });
      clientTest = res.connected
        ? { state: "ok", message: res.message ?? "Connected." }
        : { state: "fail", message: res.message ?? "Connection failed." };
    } catch (err) {
      clientTest = { state: "fail", message: err instanceof Error ? err.message : "Test failed." };
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

  // ── Remote path mappings ────────────────────────────────────
  let pathMappings = $state<RemotePathMappingView[]>([]);
  let mappingForm = $state<{ downloadClientConfigId: string; remotePath: string; localPath: string } | null>(null);
  function newMapping() {
    mappingForm = { downloadClientConfigId: downloadClients[0]?.id ?? "", remotePath: "", localPath: "" };
  }
  async function saveMapping() {
    if (!mappingForm) return;
    busy = true;
    try {
      await saveRemotePathMapping({ id: null, ...mappingForm });
      mappingForm = null;
      onMessage("Remote path mapping saved");
      await load();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to save mapping");
    } finally {
      busy = false;
    }
  }
  async function removeMapping(id: string) {
    busy = true;
    try {
      await deleteRemotePathMapping(id);
      await load();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to delete mapping");
    } finally {
      busy = false;
    }
  }
  function clientNameOf(id: string): string {
    return downloadClients.find((c) => c.id === id)?.displayName ?? "(removed client)";
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
      downloadCategory: null, allowedQualities: [], cutoffQuality: null,
      formatScores: {}, minFormatScore: 0, cutoffFormatScore: null,
    };
    profileTerms = { preferred: "", required: "", ignored: "", weighted: "", languages: "English" };
  }
  /** Switching the form's kind re-targets the root (each kind offers different libraries) and refreshes the suggested name and naming template. */
  function setProfileKind(kind: string) {
    if (!profileForm) return;
    const previousKind = profileForm.kind;
    profileForm.kind = kind as typeof profileForm.kind;
    const suitable = rootsForKind(kind);
    if (!suitable.some((r) => r.id === profileForm?.targetLibraryRootId)) {
      profileForm.targetLibraryRootId = suitable[0]?.id ?? "";
    }
    if (!profileForm.id && /^Default /.test(profileForm.displayName)) {
      profileForm.displayName = `Default ${profileKindLabels[kind] ?? kind}`;
      profileForm.isDefault = !profiles.some((p) => p.kind === kind);
    }
    // A template still at the previous kind's default is "untouched" — track the kind switch; a
    // user-edited one is left alone (mirrors how the display name tracks the kind switch).
    if (profileForm.pathTemplate === namingDefaultFor(previousKind)) {
      profileForm.pathTemplate = namingDefaultFor(kind);
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
      downloadCategory: p.downloadCategory ?? null, allowedQualities: p.allowedQualities ?? [], cutoffQuality: p.cutoffQuality ?? null,
      formatScores: { ...(p.formatScores ?? {}) }, minFormatScore: p.minFormatScore ?? 0, cutoffFormatScore: p.cutoffFormatScore ?? null,
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

  // ── Custom formats ──────────────────────────────────────────
  let formatForm = $state<CustomFormatSaveRequest | null>(null);
  const conditionTypeOptions = [
    { value: CUSTOM_FORMAT_CONDITION_TYPE.releaseTitle, label: "Release title (regex)" },
    { value: CUSTOM_FORMAT_CONDITION_TYPE.releaseGroup, label: "Release group (regex)" },
    { value: CUSTOM_FORMAT_CONDITION_TYPE.language, label: "Language name" },
    { value: CUSTOM_FORMAT_CONDITION_TYPE.quality, label: "Quality code" },
  ];
  function conditionPlaceholder(type: string): string {
    if (type === CUSTOM_FORMAT_CONDITION_TYPE.language) return "english";
    if (type === CUSTOM_FORMAT_CONDITION_TYPE.quality) return "bluray-1080p";
    return "(?i)dual|eng|english";
  }
  function newFormat() {
    formatForm = {
      id: null, kind: ENTITY_KIND.book, name: "",
      conditions: [{ type: CUSTOM_FORMAT_CONDITION_TYPE.releaseTitle, value: "", negate: false, required: false }],
    };
  }
  function editFormat(f: CustomFormatView) {
    formatForm = { id: f.id, kind: f.kind, name: f.name, conditions: f.conditions.map((c) => ({ ...c })) };
  }
  function addCondition() {
    if (!formatForm) return;
    formatForm.conditions = [...formatForm.conditions, { type: CUSTOM_FORMAT_CONDITION_TYPE.releaseTitle, value: "", negate: false, required: false }];
  }
  function removeCondition(index: number) {
    if (!formatForm || formatForm.conditions.length <= 1) return;
    formatForm.conditions = formatForm.conditions.filter((_, i) => i !== index);
  }
  async function saveFormat() {
    if (!formatForm) return;
    busy = true;
    try {
      await saveCustomFormat(formatForm);
      formatForm = null;
      onMessage("Custom format saved");
      await load();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to save custom format");
    } finally {
      busy = false;
    }
  }
  async function removeFormat(id: string) {
    busy = true;
    try {
      await deleteCustomFormat(id);
      await load();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to delete custom format");
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
      <!-- Inline connection-test feedback, shown right next to a form's Test button. -->
      {#snippet testResult(result: TestResult | null)}
        {#if result}
          <span class={cn("inline-flex items-center gap-1.5 text-xs", result.state === "ok" ? "text-[#6fd39a]" : result.state === "fail" ? "text-[#ff9a86]" : "text-text-muted")}>
            {#if result.state === "testing"}<Loader2 class="h-3.5 w-3.5 animate-spin" />
            {:else if result.state === "ok"}<CircleCheck class="h-3.5 w-3.5" />
            {:else}<CircleAlert class="h-3.5 w-3.5" />{/if}
            <span>{result.message}</span>
          </span>
        {/if}
      {/snippet}

      <!-- Indexers -->
      <section class="space-y-2">
        <div class="flex items-center justify-between">
          <h3 class="text-kicker text-text-primary">Indexers</h3>
          {#if !indexerForm}
            <Button size="sm" variant="secondary" onclick={newIndexer} class="gap-1.5"><Plus class="h-3.5 w-3.5" /> Add</Button>
          {/if}
        </div>
        {#snippet indexerEditor()}
          {#if indexerForm}
            <div class="space-y-2 rounded-sm border border-border-accent bg-surface-1 p-3">
              <div class="grid gap-2 sm:grid-cols-2">
                <label class="space-y-1"><span class="text-label text-text-muted">Indexer type</span>
                  <Select size="sm" value={indexerForm.kind} options={indexerKindOptions} onchange={setIndexerKind} /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">Name</span>
                  <TextInput size="sm" value={indexerForm.displayName} oninput={(e) => indexerForm && (indexerForm.displayName = e.currentTarget.value)} /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">Base URL</span>
                  <TextInput size="sm" value={indexerForm.baseUrl} oninput={(e) => indexerForm && (indexerForm.baseUrl = e.currentTarget.value)} placeholder={indexerForm.kind === INDEXER_KIND.prowlarr ? "https://prowlarr.example.com" : "https://indexer.example.com (or a Jackett /api path)"} /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">API key</span>
                  <TextInput size="sm" type="password" value={indexerForm.apiKey ?? ""} oninput={(e) => indexerForm && (indexerForm.apiKey = e.currentTarget.value)} placeholder={indexerForm.id ? "(unchanged)" : ""} /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">Categories</span>
                  <TextInput size="sm" value={indexerCategories} oninput={(e) => (indexerCategories = e.currentTarget.value)} placeholder="7000,8000" /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">Query limit / hour</span>
                  <TextInput size="sm" type="number" value={indexerForm.queryLimitPerHour == null ? "" : String(indexerForm.queryLimitPerHour)} oninput={(e) => indexerForm && (indexerForm.queryLimitPerHour = e.currentTarget.value ? Number(e.currentTarget.value) : null)} placeholder="unlimited" /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">Seed ratio goal</span>
                  <TextInput size="sm" type="number" value={indexerForm.seedRatio == null ? "" : String(indexerForm.seedRatio)} oninput={(e) => indexerForm && (indexerForm.seedRatio = e.currentTarget.value ? Number(e.currentTarget.value) : null)} placeholder="client default" /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">Seed time goal (minutes)</span>
                  <TextInput size="sm" type="number" value={indexerForm.seedTimeMinutes == null ? "" : String(indexerForm.seedTimeMinutes)} oninput={(e) => indexerForm && (indexerForm.seedTimeMinutes = e.currentTarget.value ? Number(e.currentTarget.value) : null)} placeholder="client default" /></label>
              </div>
              <div class="flex flex-wrap items-center justify-between gap-2">
                <div class="flex items-center gap-2">
                  <Button size="sm" variant="ghost" onclick={testIndexer} disabled={busy || indexerTest?.state === "testing"} class="gap-1.5"><PlugZap class="h-3.5 w-3.5" /> Test</Button>
                  {@render testResult(indexerTest)}
                </div>
                <div class="flex gap-1.5">
                  <Button size="sm" variant="ghost" onclick={() => (indexerForm = null)} disabled={busy}>Cancel</Button>
                  <Button size="sm" variant="primary" onclick={saveIndexer} disabled={busy || !indexerForm.displayName || !indexerForm.baseUrl}>Save</Button>
                </div>
              </div>
            </div>
          {/if}
        {/snippet}
        {#each indexers as item (item.id)}
          {#if indexerForm && indexerForm.id === item.id}
            {@render indexerEditor()}
          {:else}
            <div class="flex items-center justify-between rounded-sm border border-border-subtle bg-surface-1 px-3 py-2">
              <div class="flex items-center gap-2">
                <StatusLed status={item.enabled ? "active" : "idle"} />
                <span class="text-sm text-text-primary">{item.displayName}</span>
                <Badge variant="default">{indexerKindLabel(item.kind)}</Badge>
                <span class="text-xs text-text-muted">{item.baseUrl}</span>
                {#if item.hasApiKey}<Badge variant="default">key set</Badge>{/if}
                {#if item.disabledUntil}
                  <Badge variant="warning" title={item.lastFailureMessage ?? undefined}>backing off until {new Date(item.disabledUntil).toLocaleTimeString()}</Badge>
                {/if}
              </div>
              <div class="flex items-center gap-1">
                <Button size="sm" variant="ghost" onclick={() => editIndexer(item)} disabled={busy}><Pencil class="h-3.5 w-3.5" /></Button>
                <Button size="sm" variant="ghost" onclick={() => removeIndexer(item.id)} disabled={busy}><Trash2 class="h-3.5 w-3.5" /></Button>
              </div>
            </div>
          {/if}
        {/each}
        {#if indexerForm && !indexerForm.id}
          {@render indexerEditor()}
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
        {#snippet clientEditor()}
          {#if clientForm}
            <div class="space-y-2 rounded-sm border border-border-accent bg-surface-1 p-3">
              <div class="grid gap-2 sm:grid-cols-2">
                <label class="space-y-1"><span class="text-label text-text-muted">Client</span>
                  <Select size="sm" value={clientForm.kind} options={clientKindOptions} onchange={setClientKind} /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">Name</span>
                  <TextInput size="sm" value={clientForm.displayName} oninput={(e) => clientForm && (clientForm.displayName = e.currentTarget.value)} /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">Base URL</span>
                  <TextInput size="sm" value={clientForm.baseUrl} oninput={(e) => clientForm && (clientForm.baseUrl = e.currentTarget.value)} placeholder="http://localhost:8080" /></label>
                {#if clientForm.kind === DOWNLOAD_CLIENT_KIND.sabnzbd}
                  <label class="space-y-1"><span class="text-label text-text-muted">API key</span>
                    <TextInput size="sm" type="password" value={clientForm.apiKey ?? ""} oninput={(e) => clientForm && (clientForm.apiKey = e.currentTarget.value)} placeholder={clientForm.id ? "(unchanged)" : "from SABnzbd Config → General"} /></label>
                {/if}
                <label class="space-y-1"><span class="text-label text-text-muted">Username</span>
                  <TextInput size="sm" value={clientForm.username ?? ""} oninput={(e) => clientForm && (clientForm.username = e.currentTarget.value)} /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">Password</span>
                  <TextInput size="sm" type="password" value={clientForm.password ?? ""} oninput={(e) => clientForm && (clientForm.password = e.currentTarget.value)} placeholder={clientForm.id ? "(unchanged)" : ""} /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">Category / label</span>
                  <TextInput size="sm" value={clientForm.category} oninput={(e) => clientForm && (clientForm.category = e.currentTarget.value)} /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">Priority (lower wins)</span>
                  <TextInput size="sm" type="number" value={String(clientForm.priority ?? 25)} oninput={(e) => clientForm && (clientForm.priority = Number(e.currentTarget.value) || 25)} /></label>
                {#if clientForm.kind !== DOWNLOAD_CLIENT_KIND.sabnzbd}
                  <label class="space-y-1"><span class="text-label text-text-muted">Default seed ratio goal</span>
                    <TextInput size="sm" type="number" value={clientForm.seedRatio == null ? "" : String(clientForm.seedRatio)} oninput={(e) => clientForm && (clientForm.seedRatio = e.currentTarget.value ? Number(e.currentTarget.value) : null)} placeholder="none (client rules)" /></label>
                  <label class="space-y-1"><span class="text-label text-text-muted">Default seed time goal (minutes)</span>
                    <TextInput size="sm" type="number" value={clientForm.seedTimeMinutes == null ? "" : String(clientForm.seedTimeMinutes)} oninput={(e) => clientForm && (clientForm.seedTimeMinutes = e.currentTarget.value ? Number(e.currentTarget.value) : null)} placeholder="none (client rules)" /></label>
                {/if}
              </div>
              <div class="flex flex-wrap items-center justify-between gap-2">
                <div class="flex items-center gap-2">
                  <Button size="sm" variant="ghost" onclick={testClient} disabled={busy || clientTest?.state === "testing"} class="gap-1.5"><PlugZap class="h-3.5 w-3.5" /> Test</Button>
                  {@render testResult(clientTest)}
                </div>
                <div class="flex gap-1.5">
                  <Button size="sm" variant="ghost" onclick={() => (clientForm = null)} disabled={busy}>Cancel</Button>
                  <Button size="sm" variant="primary" onclick={saveClient} disabled={busy || !clientForm.displayName || !clientForm.baseUrl || !clientForm.category}>Save</Button>
                </div>
              </div>
            </div>
          {/if}
        {/snippet}
        {#each downloadClients as item (item.id)}
          {#if clientForm && clientForm.id === item.id}
            {@render clientEditor()}
          {:else}
            <div class="flex items-center justify-between rounded-sm border border-border-subtle bg-surface-1 px-3 py-2">
              <div class="flex items-center gap-2">
                <StatusLed status={item.enabled ? "active" : "idle"} />
                <span class="text-sm text-text-primary">{item.displayName}</span>
                <Badge variant="default">{clientKindLabel(item.kind)}</Badge>
                <span class="text-xs text-text-muted">{item.baseUrl}</span>
                <Badge variant="default">{item.category}</Badge>
              </div>
              <div class="flex items-center gap-1">
                <Button size="sm" variant="ghost" onclick={() => editClient(item)} disabled={busy}><Pencil class="h-3.5 w-3.5" /></Button>
                <Button size="sm" variant="ghost" onclick={() => removeClient(item.id)} disabled={busy}><Trash2 class="h-3.5 w-3.5" /></Button>
              </div>
            </div>
          {/if}
        {/each}
        {#if clientForm && !clientForm.id}
          {@render clientEditor()}
        {/if}
      </section>

      <!-- Remote path mappings -->
      {#if downloadClients.length > 0}
        <section class="space-y-2">
          <div class="flex items-center justify-between">
            <h3 class="text-kicker text-text-primary">Remote path mappings</h3>
            {#if !mappingForm}
              <Button size="sm" variant="secondary" onclick={newMapping} class="gap-1.5"><Plus class="h-3.5 w-3.5" /> Add</Button>
            {/if}
          </div>
          <p class="text-[0.72rem] leading-relaxed text-text-muted">
            When a download client runs on another host or in a container, its reported paths differ from Prismedia's.
            Map the client's path prefix to where the same files are visible to Prismedia.
          </p>
          {#each pathMappings as mapping (mapping.id)}
            <div class="flex items-center justify-between rounded-sm border border-border-subtle bg-surface-1 px-3 py-2">
              <div class="flex items-center gap-2 text-sm">
                <Badge variant="default">{clientNameOf(mapping.downloadClientConfigId)}</Badge>
                <span class="font-mono text-xs text-text-muted">{mapping.remotePath}</span>
                <span class="text-xs text-text-muted">→</span>
                <span class="font-mono text-xs text-text-primary">{mapping.localPath}</span>
              </div>
              <Button size="sm" variant="ghost" onclick={() => removeMapping(mapping.id)} disabled={busy}><Trash2 class="h-3.5 w-3.5" /></Button>
            </div>
          {/each}
          {#if mappingForm}
            <div class="space-y-2 rounded-sm border border-border-accent bg-surface-1 p-3">
              <div class="grid gap-2 sm:grid-cols-3">
                <label class="space-y-1"><span class="text-label text-text-muted">Client</span>
                  <Select size="sm" value={mappingForm.downloadClientConfigId} options={downloadClients.map((c) => ({ value: c.id, label: c.displayName }))} onchange={(v) => mappingForm && (mappingForm.downloadClientConfigId = v)} /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">Remote path (client's view)</span>
                  <TextInput size="sm" value={mappingForm.remotePath} oninput={(e) => mappingForm && (mappingForm.remotePath = e.currentTarget.value)} placeholder="/downloads" /></label>
                <label class="space-y-1"><span class="text-label text-text-muted">Local path (Prismedia's view)</span>
                  <TextInput size="sm" value={mappingForm.localPath} oninput={(e) => mappingForm && (mappingForm.localPath = e.currentTarget.value)} placeholder="/mnt/media/downloads" /></label>
              </div>
              <div class="flex justify-end gap-1.5">
                <Button size="sm" variant="ghost" onclick={() => (mappingForm = null)} disabled={busy}>Cancel</Button>
                <Button size="sm" variant="primary" onclick={saveMapping} disabled={busy || !mappingForm.downloadClientConfigId || !mappingForm.remotePath || !mappingForm.localPath}>Save</Button>
              </div>
            </div>
          {/if}
        </section>
      {/if}

      <!-- Custom formats -->
      <section class="space-y-2">
        <div class="flex items-center justify-between">
          <h3 class="text-kicker text-text-primary">Custom formats</h3>
          {#if !formatForm}
            <Button size="sm" variant="secondary" onclick={newFormat} class="gap-1.5"><Plus class="h-3.5 w-3.5" /> Add</Button>
          {/if}
        </div>
        <p class="text-[0.72rem] leading-relaxed text-text-muted">
          Named, scored release classifiers. Define conditions here, then score each format per profile below.
        </p>
        {#each customFormats as f (f.id)}
          <div class="flex items-center justify-between rounded-sm border border-border-subtle bg-surface-1 px-3 py-2">
            <div class="flex items-center gap-2">
              <Badge variant="default">{profileKindLabels[f.kind] ?? f.kind}</Badge>
              <span class="text-sm text-text-primary">{f.name}</span>
              <span class="text-xs text-text-muted">{f.conditions.length} condition(s)</span>
            </div>
            <div class="flex items-center gap-1">
              <Button size="sm" variant="ghost" onclick={() => editFormat(f)} disabled={busy}><Pencil class="h-3.5 w-3.5" /></Button>
              <Button size="sm" variant="ghost" onclick={() => removeFormat(f.id)} disabled={busy}><Trash2 class="h-3.5 w-3.5" /></Button>
            </div>
          </div>
        {/each}
        {#if formatForm}
          <div class="space-y-2 rounded-sm border border-border-accent bg-surface-1 p-3">
            <div class="grid gap-2 sm:grid-cols-2">
              <label class="space-y-1"><span class="text-label text-text-muted">Media kind</span>
                <Select size="sm" value={formatForm.kind} options={profileKindOptions} onchange={(v) => formatForm && (formatForm.kind = v as typeof formatForm.kind)} /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Name</span>
                <TextInput size="sm" value={formatForm.name} oninput={(e) => formatForm && (formatForm.name = e.currentTarget.value)} placeholder="Remux Tier" /></label>
            </div>
            <div class="space-y-1.5">
              <span class="text-label text-text-muted">Conditions</span>
              {#each formatForm.conditions as condition, i (i)}
                <div class="flex flex-wrap items-center gap-2 rounded-sm border border-border-subtle bg-surface-1 px-2 py-1.5">
                  <Select size="sm" value={condition.type} options={conditionTypeOptions} onchange={(v) => formatForm && (formatForm.conditions[i].type = v as CustomFormatConditionView["type"])} />
                  <TextInput size="sm" value={condition.value} oninput={(e) => formatForm && (formatForm.conditions[i].value = e.currentTarget.value)} placeholder={conditionPlaceholder(condition.type)} class="flex-1" />
                  <label class="flex items-center gap-1.5"><Checkbox checked={condition.negate} onchange={(e) => formatForm && (formatForm.conditions[i].negate = e.currentTarget.checked)} /><span class="text-[0.72rem] text-text-muted">Negate</span></label>
                  <label class="flex items-center gap-1.5"><Checkbox checked={condition.required} onchange={(e) => formatForm && (formatForm.conditions[i].required = e.currentTarget.checked)} /><span class="text-[0.72rem] text-text-muted">Required</span></label>
                  {#if formatForm.conditions.length > 1}
                    <Button size="sm" variant="ghost" onclick={() => removeCondition(i)} disabled={busy}><Trash2 class="h-3.5 w-3.5" /></Button>
                  {/if}
                </div>
              {/each}
              <Button size="sm" variant="secondary" onclick={addCondition} disabled={busy} class="gap-1.5"><Plus class="h-3.5 w-3.5" /> Add condition</Button>
            </div>
            <div class="flex justify-end gap-1.5">
              <Button size="sm" variant="ghost" onclick={() => (formatForm = null)} disabled={busy}>Cancel</Button>
              <Button size="sm" variant="primary" onclick={saveFormat} disabled={busy || !formatForm.name || formatForm.conditions.some((c) => !c.value)}>Save</Button>
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
              <label class="space-y-1 sm:col-span-2"><span class="text-label text-text-muted">Naming template</span>
                <TextInput size="sm" value={profileForm.pathTemplate} placeholder={namingDefaultFor(profileForm.kind)} oninput={(e) => profileForm && (profileForm.pathTemplate = e.currentTarget.value)} />
                <span class="block font-mono text-[0.68rem] leading-relaxed text-text-muted">{namingHintFor(profileForm.kind)}</span></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Import mode</span>
                <Select size="sm" value={profileForm.importMode} options={importModeOptions} onchange={(v) => profileForm && (profileForm.importMode = v as typeof profileForm.importMode)} /></label>
              <label class="space-y-1"><span class="text-label text-text-muted">Download category</span>
                <TextInput size="sm" value={profileForm.downloadCategory ?? ""} oninput={(e) => profileForm && (profileForm.downloadCategory = e.currentTarget.value || null)} placeholder="client default" /></label>
              {#if qualityLadderFor(profileForm.kind).length > 0}
                <div class="space-y-1 sm:col-span-2">
                  <span class="text-label text-text-muted">Allowed qualities (none = all)</span>
                  <div class="flex flex-wrap gap-1.5">
                    {#each qualityLadderFor(profileForm.kind) as code (code)}
                      <button type="button"
                        class={cn(
                          "rounded-xs border px-2 py-0.5 font-mono text-[0.7rem] transition-colors",
                          (profileForm.allowedQualities ?? []).includes(code)
                            ? "border-border-accent bg-surface-2 text-text-primary"
                            : "border-border-subtle bg-surface-1 text-text-muted hover:text-text-primary",
                        )}
                        onclick={() => toggleAllowedQuality(code)}>{code}</button>
                    {/each}
                  </div>
                </div>
                <label class="space-y-1"><span class="text-label text-text-muted">Upgrade cutoff quality</span>
                  <Select size="sm" value={profileForm.cutoffQuality ?? ""} options={[{ value: "", label: "None (fulfill at any allowed quality)" }, ...qualityLadderFor(profileForm.kind).map((code) => ({ value: code, label: code }))]} onchange={(v) => profileForm && (profileForm.cutoffQuality = v || null)} /></label>
              {/if}
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
            {#if formatsForKind.length > 0}
              <div class="space-y-2">
                <div class="space-y-0.5">
                  <span class="text-label text-text-muted">Custom format scores</span>
                  <p class="text-[0.72rem] leading-relaxed text-text-muted">100 points equals one preferred term; negatives act as a soft ban.</p>
                </div>
                <div class="space-y-1.5">
                  {#each formatsForKind as f (f.id)}
                    <div class="flex items-center justify-between gap-2 rounded-sm border border-border-subtle bg-surface-1 px-3 py-1.5">
                      <span class="text-sm text-text-primary">{f.name}</span>
                      <TextInput size="sm" type="number" class="w-24" value={String((profileForm.formatScores ?? {})[f.id] ?? 0)} oninput={(e) => setFormatScore(f.id, e.currentTarget.value)} placeholder="0" />
                    </div>
                  {/each}
                </div>
                <div class="grid gap-2 sm:grid-cols-2">
                  <label class="space-y-1"><span class="text-label text-text-muted">Minimum format score<span class="ml-1 text-text-muted">— reject releases scoring below this</span></span>
                    <TextInput size="sm" type="number" value={String(profileForm.minFormatScore)} oninput={(e) => profileForm && (profileForm.minFormatScore = Number(e.currentTarget.value) || 0)} /></label>
                  <label class="space-y-1"><span class="text-label text-text-muted">Cutoff format score<span class="ml-1 text-text-muted">— keep upgrading until a release reaches this score</span></span>
                    <TextInput size="sm" type="number" value={profileForm.cutoffFormatScore == null ? "" : String(profileForm.cutoffFormatScore)} oninput={(e) => profileForm && (profileForm.cutoffFormatScore = e.currentTarget.value ? Number(e.currentTarget.value) : null)} placeholder="none" /></label>
                </div>
              </div>
            {/if}
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
