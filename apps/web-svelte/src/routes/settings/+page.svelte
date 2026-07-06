<script lang="ts">
  import { onMount } from "svelte";
  import {
    Captions,
    Film,
    Loader2,
    ScanSearch,
    Settings as SettingsIcon,
    ShieldUser,
    UsersRound,
  } from "@lucide/svelte";
  import { goto } from "$app/navigation";
  import { Badge, Button, Checkbox, Panel, StatusLed, TextInput, Toggle, cn } from "@prismedia/ui-svelte";
  import {
    fetchLibraryConfig,
    fetchLibraryRoots,
    updateSetting,
    type LibraryRoot,
    type SettingDescriptor,
    type SettingsCatalogResponse,
    type SettingValue,
  } from "$lib/api/settings";
  import {
    catalogToLibrarySettings,
    defaultLibrarySettings,
    findSetting,
    findSettingsGroup,
    replaceSetting,
    settingKeys,
    settingsInGroup,
    valueAsBoolean,
    valueAsString,
  } from "$lib/settings/app-settings";
  import { useSession } from "$lib/stores/session.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import SettingsControl from "$lib/components/settings/SettingsControl.svelte";
  import AcquisitionSection from "$lib/components/settings/AcquisitionSection.svelte";
  import AutoIdentifySection from "$lib/components/settings/AutoIdentifySection.svelte";
  import DatabaseBackupsSection from "$lib/components/settings/DatabaseBackupsSection.svelte";
  import DiagnosticsSection from "$lib/components/settings/DiagnosticsSection.svelte";
  import TranscodeCacheSection from "$lib/components/settings/TranscodeCacheSection.svelte";
  import WatchedLibrariesSection from "$lib/components/settings/WatchedLibrariesSection.svelte";
  import SubtitleCaptionOverlay from "$lib/components/SubtitleCaptionOverlay.svelte";
  import type {
    SubtitleAppearance,
    SubtitleDisplayStyle,
  } from "$lib/player/subtitle-types";

  type PageProps = {
    data?: {
      config?: { settings: SettingsCatalogResponse; roots: LibraryRoot[] } | null;
      scraperCount?: number;
    };
  };

  let { data = {} }: PageProps = $props();

  const session = useSession();

  let catalog = $state<SettingsCatalogResponse | null>(null);
  let roots = $state<LibraryRoot[]>([]);

  let savedMetadataStorageDedicated = $state(defaultLibrarySettings.metadataStorageDedicated);
  let pendingMetadataStorageDedicated = $state<boolean | null>(null);
  let message = $state<string | null>(null);
  let error = $state<string | null>(null);

  let metadataStorageDialogOpen = $state(false);
  let metadataStorageBusy = $state(false);

  const effectiveSettings = $derived(catalogToLibrarySettings(catalog));
  const subtitleAppearance = $derived<SubtitleAppearance>({
    style: normalizeSubtitleStyle(effectiveSettings.subtitleStyle),
    fontScale: effectiveSettings.subtitleFontScale,
    positionPercent: effectiveSettings.subtitlePositionPercent,
    opacity: effectiveSettings.subtitleOpacity,
  });

  // Subtitle settings split into behavior (rows) and appearance (left column)
  const subtitleBehaviorKeys: readonly string[] = [settingKeys.subtitlesAutoEnable, settingKeys.subtitlesPreferredLanguages];
  const subtitleAppearanceKeys: readonly string[] = [
    settingKeys.subtitlesStyle,
    settingKeys.subtitlesFontScale,
    settingKeys.subtitlesPositionPercent,
    settingKeys.subtitlesOpacity,
  ];
  const subtitleBehavior = $derived(
    settingsInGroup(catalog, "subtitles").filter((s) => subtitleBehaviorKeys.includes(s.key)),
  );
  const subtitleAppearanceSettings = $derived(
    settingsInGroup(catalog, "subtitles").filter((s) => subtitleAppearanceKeys.includes(s.key)),
  );

  // Subtitle style setting needs custom rendering
  const subtitleStyleSetting = $derived(findSetting(catalog, settingKeys.subtitlesStyle));
  const subtitleAppearanceSliders = $derived(
    subtitleAppearanceSettings.filter((s) => s.key !== settingKeys.subtitlesStyle),
  );

  $effect(() => {
    if (!data.config) return;
    catalog = data.config.settings;
    roots = data.config.roots;
    savedMetadataStorageDedicated = effectiveMetadataStorageValue();
  });

  $effect(() => {
    if (!metadataStorageDialogOpen) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !metadataStorageBusy) {
        closeMetadataStorageDialogCancel();
      }
    };
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  });

  onMount(() => {
    void loadConfig();
  });

  function normalizeSubtitleStyle(value: string): SubtitleDisplayStyle {
    if (value === "classic" || value === "outline") return value;
    return "stylized";
  }

  function generationControls(): SettingDescriptor[] {
    return [
      ...settingsInGroup(catalog, "scan"),
      ...settingsInGroup(catalog, "collections"),
      ...settingsInGroup(catalog, "taxonomy"),
      ...settingsInGroup(catalog, "generation"),
      ...settingsInGroup(catalog, "jobs"),
    ];
  }

  function effectiveMetadataStorageValue(): boolean {
    return valueAsBoolean(
      findSetting(catalog, settingKeys.generationMetadataStorageDedicated)?.value,
      defaultLibrarySettings.metadataStorageDedicated,
    );
  }

  function flashMessage(m: string, ms = 2000) {
    message = m;
    setTimeout(() => {
      if (message === m) message = null;
    }, ms);
  }

  function setError(m: string | null) {
    error = m;
  }

  async function loadConfig() {
    if (!session.canManageServer) return;
    try {
      if (session.isAdmin) {
        const response = await fetchLibraryConfig();
        catalog = response.settings;
        roots = response.roots;
        savedMetadataStorageDedicated = effectiveMetadataStorageValue();
      } else {
        // Library creators manage only their own roots; the settings catalog is admin-only.
        roots = await fetchLibraryRoots();
      }
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load settings");
    }
  }

  function applyLocalSettingValue(key: string, value: SettingValue) {
    const current = findSetting(catalog, key);
    if (!current) return;
    catalog = replaceSetting(catalog, {
      ...current,
      value,
      isDefault: value === current.defaultValue,
    });
  }

  async function autoSaveSetting(key: string, value: SettingValue): Promise<boolean> {
    try {
      const updated = await updateSetting(key, value);
      catalog = replaceSetting(catalog, updated);
      if (key === settingKeys.generationMetadataStorageDedicated) {
        savedMetadataStorageDedicated = valueAsBoolean(updated.value, savedMetadataStorageDedicated);
      }
      setError(null);
      flashMessage("Setting saved.");
      return true;
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to save setting");
      await loadConfig();
      return false;
    }
  }

  function handleSettingCommit(key: string, value: SettingValue) {
    if (
      key === settingKeys.generationMetadataStorageDedicated &&
      typeof value === "boolean"
    ) {
      handleMetadataStorageToggle(value);
      return;
    }
    void autoSaveSetting(key, value);
  }

  function handleMetadataStorageToggle(checked: boolean) {
    if (checked === savedMetadataStorageDedicated) return;
    pendingMetadataStorageDedicated = checked;
    applyLocalSettingValue(settingKeys.generationMetadataStorageDedicated, checked);
    metadataStorageDialogOpen = true;
  }

  function revertMetadataStorageToggle() {
    applyLocalSettingValue(
      settingKeys.generationMetadataStorageDedicated,
      savedMetadataStorageDedicated,
    );
    pendingMetadataStorageDedicated = null;
  }

  function closeMetadataStorageDialogCancel() {
    metadataStorageDialogOpen = false;
    revertMetadataStorageToggle();
  }

  async function confirmMetadataStorageLeaveInPlace() {
    if (pendingMetadataStorageDedicated === null) return;
    metadataStorageBusy = true;
    setError(null);
    try {
      const saved = await autoSaveSetting(
        settingKeys.generationMetadataStorageDedicated,
        pendingMetadataStorageDedicated,
      );
      if (saved) {
        pendingMetadataStorageDedicated = null;
        metadataStorageDialogOpen = false;
        flashMessage("Setting saved.", 2500);
      }
    } finally {
      metadataStorageBusy = false;
    }
  }

  async function confirmMetadataStorageMoveFiles() {
    if (pendingMetadataStorageDedicated === null) return;
    metadataStorageBusy = true;
    setError(null);
    try {
      const saved = await autoSaveSetting(
        settingKeys.generationMetadataStorageDedicated,
        pendingMetadataStorageDedicated,
      );
      if (saved) {
        pendingMetadataStorageDedicated = null;
        metadataStorageDialogOpen = false;
        flashMessage(
          "Setting saved. Moving existing preview files will return with the media pipeline.",
          6000,
        );
      }
    } finally {
      metadataStorageBusy = false;
    }
  }
</script>

<svelte:head>
  <title>Settings · Prismedia</title>
</svelte:head>

{#if !session.canManageServer}
  <StatePlaceholder
    icon={ShieldUser}
    title="Administrator access required"
    description="Server settings are managed by administrators."
  />
{:else}
<div class="space-y-8">
  <!-- Page header -->
  <div>
    <h1 class="flex items-center gap-2.5">
      <SettingsIcon class="h-5 w-5 text-text-accent" />
      Settings
    </h1>
    <p class="mt-1 text-[0.78rem] text-text-muted">
      Configure libraries, playback defaults, and the generation pipeline
    </p>
  </div>

  <!-- Toast messages -->
  {#if error}
    <div class="surface-panel border-l-2 border-status-error px-4 py-2.5 text-sm text-status-error-text">
      {error}
    </div>
  {/if}
  {#if message && !error}
    <div class="surface-panel border-l-2 border-status-success px-4 py-2.5 text-sm text-status-success-text">
      {message}
    </div>
  {/if}

  <!-- ── Watched Libraries ── -->
  <WatchedLibrariesSection
    bind:roots
    onRootsChanged={loadConfig}
    onError={setError}
    onMessage={flashMessage}
  />

  <!-- ── Users ── -->
  {#if session.isAdmin}
    <Panel>
      <div class="flex flex-wrap items-center justify-between gap-3 p-5">
        <div class="flex items-center gap-2.5">
          <UsersRound class="h-4 w-4 text-text-accent" />
          <div>
            <h2 class="text-kicker text-text-primary">Users</h2>
            <p class="text-[0.68rem] text-text-muted">
              Accounts, passwords, NSFW permissions, and per-library access. The same
              credentials sign in to the web app, Jellyfin clients, and OPDS readers.
            </p>
          </div>
        </div>
        <Button variant="secondary" size="sm" onclick={() => goto("/settings/users")}>
          Manage users
        </Button>
      </div>
    </Panel>
  {/if}

{#if session.isAdmin}
    <!-- ── Acquisition ── -->
    <AcquisitionSection onError={setError} onMessage={flashMessage} />

    <!-- ── Playback ── -->
    <Panel>
      <div class="p-5 space-y-5">
        <div class="flex items-center gap-2.5">
          <Film class="h-4 w-4 text-text-accent" />
          <div>
            <h2 class="text-kicker text-text-primary">Playback</h2>
            <p class="text-[0.68rem] text-text-muted">
              Defaults applied to the video player when a video loads
            </p>
          </div>
        </div>

        <div class="divide-y divide-border-subtle px-1">
          {#each settingsInGroup(catalog, "playback") as setting (setting.key)}
            <SettingsControl {setting} onCommit={handleSettingCommit} />
          {/each}
          {#each settingsInGroup(catalog, "hls") as setting (setting.key)}
            <SettingsControl {setting} onCommit={handleSettingCommit} />
          {/each}
        </div>
      </div>
    </Panel>

    <!-- ── Subtitles ── -->
    <Panel>
      <div class="p-5 space-y-5">
        <div class="flex items-center gap-2.5">
          <Captions class="h-4 w-4 text-text-accent" />
          <div>
            <h2 class="text-kicker text-text-primary">Subtitles</h2>
            <p class="text-[0.68rem] text-text-muted">
              Caption behavior and appearance defaults for video playback
            </p>
          </div>
        </div>

        <!-- Behavior rows: auto-enable + language -->
        <div class="divide-y divide-border-subtle px-1">
          {#each subtitleBehavior as setting (setting.key)}
            <SettingsControl {setting} onCommit={handleSettingCommit} />
          {/each}
        </div>

        <!-- Appearance: style controls left, preview right -->
        <div class="grid gap-5 lg:grid-cols-2">
          <!-- Left: style + sliders -->
          <div class="space-y-4">
            <!-- Style selector (expressive buttons) -->
            {#if subtitleStyleSetting}
              <div>
                <div class="text-label text-text-muted mb-2">{subtitleStyleSetting.label}</div>
                <div class="grid grid-cols-3 gap-2">
                  {#each subtitleStyleSetting.options as option (option.value)}
                    {@const active = valueAsString(subtitleStyleSetting.value) === option.value}
                    <button
                      type="button"
                      onclick={() => handleSettingCommit(subtitleStyleSetting.key, option.value)}
                      class={cn(
                        "rounded-sm border p-2.5 text-left transition-all duration-fast",
                        active
                          ? "border-border-accent bg-surface-3 text-accent-400 shadow-[var(--shadow-glow-accent)]"
                          : "border-border-default bg-surface-1 text-text-muted hover:border-border-subtle hover:bg-surface-2/60 hover:text-text-primary",
                      )}
                    >
                      <span class="block text-[0.72rem] font-medium uppercase tracking-wider">
                        {option.label}
                      </span>
                      {#if option.description}
                        <span class="mt-0.5 block text-[0.62rem] leading-snug text-text-muted">
                          {option.description}
                        </span>
                      {/if}
                    </button>
                  {/each}
                </div>
              </div>
            {/if}

            <!-- Sliders: font scale, position, opacity -->
            <div class="divide-y divide-border-subtle">
              {#each subtitleAppearanceSliders as setting (setting.key)}
                <SettingsControl {setting} onCommit={handleSettingCommit} />
              {/each}
            </div>
          </div>

          <!-- Right: live preview -->
          <div class="space-y-2">
            <div class="text-label text-text-muted">Preview</div>
            <div class="relative aspect-video w-full overflow-hidden rounded-sm border border-border-subtle bg-black">
              <div
                class="absolute inset-0 bg-[linear-gradient(135deg,#1a1f2b_0%,#0e1118_45%,#2a1f14_100%)]"
              ></div>
              <div
                class="absolute inset-0 opacity-[0.08]"
                style:background-image="repeating-linear-gradient(90deg, rgba(255,255,255,0.6) 0, rgba(255,255,255,0.6) 1px, transparent 1px, transparent 32px), repeating-linear-gradient(0deg, rgba(255,255,255,0.6) 0, rgba(255,255,255,0.6) 1px, transparent 1px, transparent 32px)"
              ></div>
              <div class="absolute inset-x-0 bottom-0 h-12 bg-gradient-to-t from-black/80 to-transparent"></div>
              <SubtitleCaptionOverlay
                text="This is how your subtitles will look."
                appearance={subtitleAppearance}
                alwaysVisible
              />
            </div>
          </div>
        </div>
      </div>
    </Panel>

    <!-- ── Generation Pipeline ── -->
    <Panel>
      <div class="p-5 space-y-5">
        <div class="flex items-center gap-2.5">
          <ScanSearch class="h-4 w-4 text-text-accent" />
          <div>
            <h2 class="text-kicker text-text-primary">Generation Pipeline</h2>
            <p class="text-[0.68rem] text-text-muted">
              Control automatic scanning and how new files are enriched
            </p>
          </div>
        </div>

        <div class="divide-y divide-border-subtle px-1">
          {#each generationControls() as setting (setting.key)}
            <SettingsControl {setting} onCommit={handleSettingCommit} />
          {/each}
        </div>
      </div>
    </Panel>

    <!-- ── Auto Identify ── -->
    <AutoIdentifySection {catalog} onCommit={handleSettingCommit} />

    <!-- ── Transcode Cache ── -->
    <TranscodeCacheSection {catalog} onCommit={handleSettingCommit} />

    <!-- ── Database Backups ── -->
    <DatabaseBackupsSection />

    <!-- ── Diagnostics ── -->
    <DiagnosticsSection />
{/if}
</div>

<!-- Metadata storage relocation dialog -->
{#if metadataStorageDialogOpen}
  <div class="fixed inset-0 z-50 flex items-center justify-center">
    <button
      type="button"
      class="absolute inset-0 bg-black/80 backdrop-blur-sm"
      onclick={metadataStorageBusy ? undefined : closeMetadataStorageDialogCancel}
      aria-label="Close dialog"
    ></button>
    <div class="relative surface-elevated mx-4 w-full max-w-md space-y-4 border border-border-subtle p-6">
      <h3 class="text-base font-heading font-semibold text-text-primary">
        Relocate existing video assets?
      </h3>
      <p class="text-[0.78rem] leading-relaxed text-text-muted">
        You changed where new thumbnails, preview clips, sprites, and trickplay files are
        stored. Move files that are already on disk to the new location, or leave them in place.
      </p>
      <div class="flex flex-col gap-2">
        <Button
          type="button"
          variant="primary"
          disabled={metadataStorageBusy}
          onclick={() => void confirmMetadataStorageMoveFiles()}
          class="w-full gap-2 px-3.5 py-2.5 text-[0.8rem]"
        >
          {#if metadataStorageBusy}
            <StatusLed status="accent" size="sm" pulse />
            <Loader2
              class="h-4 w-4 animate-spin text-accent-300 drop-shadow-[0_0_6px_rgba(199,155,92,0.35)]"
            />
          {/if}
          Move existing files
        </Button>
        <Button
          type="button"
          variant="secondary"
          disabled={metadataStorageBusy}
          onclick={() => void confirmMetadataStorageLeaveInPlace()}
          class="no-lift w-full border-border-subtle bg-surface-2/40 px-3.5 py-2.5 text-[0.8rem] text-text-secondary hover:border-border-accent/25"
        >
          Leave files in place
        </Button>
        <Button
          type="button"
          variant="ghost"
          disabled={metadataStorageBusy}
          onclick={closeMetadataStorageDialogCancel}
          class="h-auto w-full px-3.5 py-2 text-[0.75rem]"
        >
          Cancel
        </Button>
      </div>
    </div>
  </div>
{/if}
{/if}
