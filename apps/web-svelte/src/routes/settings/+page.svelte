<script lang="ts">
  import { resolve } from "$app/paths";
  import { onMount } from "svelte";
  import {
    Captions,
    ChevronRight,
    Database,
    Eye,
    Film,
    Flame,
    Loader2,
    Package,
    ScanSearch,
    Settings as SettingsIcon,
    Shield,
  } from "@lucide/svelte";
  import { Button, StatusLed, cn } from "@prismedia/ui-svelte";
  import {
    fetchLibraryConfig,
    updateSetting,
    type LibraryRoot,
    type SettingDescriptor,
    type SettingsCatalogResponse,
    type SettingValue,
  } from "$lib/api/prismedia";
  import {
    catalogToLibrarySettings,
    defaultLibrarySettings,
    findSetting,
    findSettingsGroup,
    replaceSetting,
    settingKeys,
    settingsInGroup,
    valueAsBoolean,
  } from "$lib/settings/app-settings";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import SettingsControl from "$lib/components/settings/SettingsControl.svelte";
  import DiagnosticsSection from "$lib/components/settings/DiagnosticsSection.svelte";
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

  const nsfw = useNsfw();

  let catalog = $state<SettingsCatalogResponse | null>(null);
  let roots = $state<LibraryRoot[]>([]);
  let scraperCount = $state(0);

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
  const visibilityDefaultModeSetting = $derived(
    findSetting(catalog, settingKeys.visibilityDefaultMode),
  );
  const visibilityLanAutoEnableSetting = $derived(
    findSetting(catalog, settingKeys.visibilityLanAutoEnable),
  );

  $effect(() => {
    if (!data.config) return;
    catalog = data.config.settings;
    roots = data.config.roots;
    scraperCount = data.scraperCount ?? 0;
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
    try {
      const response = await fetchLibraryConfig();
      catalog = response.settings;
      roots = response.roots;
      scraperCount = data.scraperCount ?? 0;
      savedMetadataStorageDedicated = effectiveMetadataStorageValue();
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

<div class="space-y-6">
  <div>
    <h1 class="flex items-center gap-2.5">
      <SettingsIcon class="h-5 w-5 text-text-accent" />
      Settings
    </h1>
    <p class="mt-1 text-[0.78rem] text-text-muted">
      Configure libraries, playback defaults, and the generation pipeline
    </p>
  </div>

  {#if error}
    <div
      class="surface-card no-lift border-l-2 border-status-error px-3 py-2 text-sm text-status-error-text"
    >
      {error}
    </div>
  {/if}
  {#if message && !error}
    <div
      class="surface-card no-lift border-l-2 border-status-success px-3 py-2 text-sm text-status-success-text"
    >
      {message}
    </div>
  {/if}

  <WatchedLibrariesSection
    bind:roots
    onRootsChanged={loadConfig}
    onError={setError}
    onMessage={flashMessage}
  />

  <div class="border-t border-border-subtle"></div>

  <section class="space-y-3">
    <div class="flex items-center gap-2.5 px-1">
      <Eye class="h-4 w-4 text-text-accent" />
      <div>
        <h2 class="text-sm font-semibold tracking-wide font-heading text-text-primary uppercase">
          Content Visibility
        </h2>
        <p class="text-[0.68rem] text-text-muted">
          {findSettingsGroup(catalog, "visibility")?.description ??
            "Control how adult content is displayed across the application"}
        </p>
      </div>
    </div>

    <div class="grid gap-2 md:grid-cols-2 md:items-stretch">
      <div class="surface-card no-lift flex h-full flex-col gap-3 p-3.5">
        <div>
          <div class="control-label">This device</div>
          <p class="text-[0.68rem] text-text-muted">
            Stored in this browser. Does not affect stored data.
          </p>
        </div>

        <div class="flex border border-border-default bg-surface-1 p-1 shadow-[inset_0_2px_6px_rgba(0,0,0,0.5)]">
          <button
            type="button"
            onclick={() => nsfw.setMode("off")}
            class={cn(
              "flex flex-1 flex-col items-center justify-center gap-1.5 border py-2.5 transition-all duration-fast",
              nsfw.mode === "off"
                ? "border-border-subtle bg-surface-3 text-text-primary shadow-card"
                : "border-transparent text-text-muted hover:bg-surface-2/50 hover:text-text-primary",
            )}
          >
            <Shield class={cn("h-4 w-4", nsfw.mode === "off" && "text-info-text")} />
            <span class="text-[0.75rem] font-medium">Off (SFW)</span>
          </button>
          <button
            type="button"
            onclick={() => nsfw.setMode("show")}
            class={cn(
              "flex flex-1 flex-col items-center justify-center gap-1.5 border py-2.5 transition-all duration-fast",
              nsfw.mode === "show"
                ? "border-border-accent bg-surface-3 text-accent-400 shadow-[var(--shadow-glow-accent)]"
                : "border-transparent text-text-muted hover:bg-surface-2/50 hover:text-text-primary",
            )}
          >
            <Flame class={cn("h-4 w-4", nsfw.mode === "show" && "text-accent-500")} />
            <span class="text-[0.75rem] font-medium">Show</span>
          </button>
        </div>

        <div class="border border-border-subtle bg-surface-2/50 p-2.5 text-[0.7rem] text-text-muted">
          {#if nsfw.mode === "off"}
            Adult content is hidden on this device.
          {:else}
            All content is displayed on this device.
          {/if}
        </div>
      </div>

      {#if visibilityDefaultModeSetting}
        <SettingsControl setting={visibilityDefaultModeSetting} onCommit={handleSettingCommit} />
      {/if}
      {#if visibilityLanAutoEnableSetting}
        <SettingsControl setting={visibilityLanAutoEnableSetting} onCommit={handleSettingCommit} />
      {/if}
    </div>
  </section>

  <div class="border-t border-border-subtle"></div>

  <section class="space-y-3">
    <div class="flex items-center gap-2.5 px-1">
      <Film class="h-4 w-4 text-text-accent" />
      <div>
        <h2 class="text-sm font-semibold tracking-wide font-heading text-text-primary uppercase">
          Playback
        </h2>
        <p class="text-[0.68rem] text-text-muted">
          Defaults applied to the video player when a video loads
        </p>
      </div>
    </div>

    <div class="grid gap-2 md:grid-cols-2 md:items-stretch">
      {#each settingsInGroup(catalog, "playback") as setting (setting.key)}
        <SettingsControl {setting} onCommit={handleSettingCommit} />
      {/each}
      {#each settingsInGroup(catalog, "hls") as setting (setting.key)}
        <SettingsControl {setting} onCommit={handleSettingCommit} />
      {/each}
    </div>
  </section>

  <div class="border-t border-border-subtle"></div>

  <section class="space-y-3">
    <div class="flex items-center gap-2.5 px-1">
      <Captions class="h-4 w-4 text-text-accent" />
      <div>
        <h2 class="text-sm font-semibold tracking-wide font-heading text-text-primary uppercase">
          Subtitles
        </h2>
        <p class="text-[0.68rem] text-text-muted">
          Defaults applied to the video player when a video has subtitle tracks
        </p>
      </div>
    </div>

    <div class="grid gap-2 md:grid-cols-2 md:items-stretch">
      {#each settingsInGroup(catalog, "subtitles") as setting (setting.key)}
        <SettingsControl {setting} onCommit={handleSettingCommit} />
      {/each}

      <div class="surface-card no-lift flex flex-col p-3.5 md:col-span-2">
        <div>
          <div class="control-label">Preview</div>
          <p class="mt-1 text-[0.68rem] text-text-muted">
            Shows how captions will render on top of a video.
          </p>
        </div>
        <div class="relative mt-3 aspect-video w-full overflow-hidden border border-border-subtle bg-black">
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
  </section>

  <div class="border-t border-border-subtle"></div>

  <section class="space-y-3">
    <div class="flex items-center gap-2.5 px-1">
      <Database class="h-4 w-4 text-text-accent" />
      <div>
        <h2 class="text-sm font-semibold tracking-wide font-heading text-text-primary uppercase">
          Metadata Providers
        </h2>
        <p class="text-[0.68rem] text-text-muted">
          Manage identification plugins, scrapers, and StashBox endpoints
        </p>
      </div>
    </div>

    <a href={resolve("/plugins")} class="group block">
      <div
        class={cn(
          "surface-card no-lift p-3.5 transition-all duration-normal",
          "hover:border-border-accent hover:shadow-[var(--shadow-glow-accent)]",
        )}
      >
        <div class="flex items-center justify-between">
          <div class="flex items-center gap-3">
            <Package class="h-4 w-4 text-text-muted" />
            <div>
              <div class="flex items-center gap-2">
                <span class="text-[0.82rem] font-medium transition-colors duration-fast group-hover:text-text-accent">
                  Plugins
                </span>
                <span class="pill-accent px-1.5 py-0.5 text-[0.55rem]">{scraperCount}</span>
              </div>
              <p class="text-[0.65rem] text-text-disabled">
                Manage scrapers, StashBox endpoints, and identification plugins
              </p>
            </div>
          </div>
          <ChevronRight class="h-4 w-4 text-text-disabled transition-all duration-fast group-hover:translate-x-0.5 group-hover:text-text-accent" />
        </div>
      </div>
    </a>
  </section>

  <div class="border-t border-border-subtle"></div>

  <section class="space-y-3">
    <div class="flex items-center gap-2.5 px-1">
      <ScanSearch class="h-4 w-4 text-text-accent" />
      <div>
        <h2 class="text-sm font-semibold tracking-wide font-heading text-text-primary uppercase">
          Generation Pipeline
        </h2>
        <p class="text-[0.68rem] text-text-muted">
          Control automatic scanning and how new files are enriched
        </p>
      </div>
    </div>

    <div class="grid gap-2 md:grid-cols-2 md:items-stretch">
      {#each generationControls() as setting (setting.key)}
        <SettingsControl {setting} onCommit={handleSettingCommit} />
      {/each}
    </div>
  </section>

  <DiagnosticsSection />
</div>

{#if metadataStorageDialogOpen}
  <div class="fixed inset-0 z-50 flex items-center justify-center">
    <button
      type="button"
      class="absolute inset-0 bg-black/80 backdrop-blur-sm"
      onclick={metadataStorageBusy ? undefined : closeMetadataStorageDialogCancel}
      aria-label="Close dialog"
    ></button>
    <div
      class="relative surface-elevated mx-4 w-full max-w-md space-y-4 border border-border-subtle p-6"
    >
      <h3 class="text-base font-heading font-semibold text-text-primary">
        Relocate existing video assets?
      </h3>
      <p class="text-[0.78rem] leading-relaxed text-text-muted">
        You changed where new thumbnails, preview clips, sprites, and trickplay files are
        stored. Move files that are already on disk to the new location, or leave them in
        place.
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
