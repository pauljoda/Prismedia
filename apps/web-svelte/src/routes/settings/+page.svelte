<script lang="ts">
  import { onMount } from "svelte";
  import {
    Captions,
    Clipboard,
    Eye,
    EyeOff,
    Film,
    Flame,
    KeyRound,
    Loader2,
    RefreshCw,
    ScanSearch,
    Settings as SettingsIcon,
    Shield,
    Trash2,
    UserPlus,
  } from "@lucide/svelte";
  import { Badge, Button, Checkbox, Panel, StatusLed, TextInput, Toggle, cn } from "@prismedia/ui-svelte";
  import {
    fetchLibraryConfig,
    updateSetting,
    type LibraryRoot,
    type SettingDescriptor,
    type SettingsCatalogResponse,
    type SettingValue,
  } from "$lib/api/settings";
  import {
    createJellyfinProfile,
    deleteJellyfinProfile,
    fetchApiKey,
    fetchJellyfinProfiles,
    regenerateApiKey,
    updateJellyfinProfile,
    type ApiKeyResponse,
    type JellyfinProfile,
  } from "$lib/api/security";
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
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import SettingsControl from "$lib/components/settings/SettingsControl.svelte";
  import AutoIdentifySection from "$lib/components/settings/AutoIdentifySection.svelte";
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

  const nsfw = useNsfw();

  let catalog = $state<SettingsCatalogResponse | null>(null);
  let roots = $state<LibraryRoot[]>([]);
  let apiKey = $state<ApiKeyResponse | null>(null);
  let apiKeyRevealed = $state(false);
  let jellyfinProfiles = $state<JellyfinProfile[]>([]);
  let profileUsername = $state("");
  let profileDisplayName = $state("");
  let profileAllowSfw = $state(true);
  let profileAllowNsfw = $state(false);

  let savedMetadataStorageDedicated = $state(defaultLibrarySettings.metadataStorageDedicated);
  let pendingMetadataStorageDedicated = $state<boolean | null>(null);
  let message = $state<string | null>(null);
  let error = $state<string | null>(null);
  let securityBusy = $state(false);
  let profileBusy = $state(false);

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
    void loadSecurity();
  });

  function normalizeSubtitleStyle(value: string): SubtitleDisplayStyle {
    if (value === "classic" || value === "outline") return value;
    return "stylized";
  }

  function generationControls(): SettingDescriptor[] {
    return [
      ...settingsInGroup(catalog, "scan"),
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
    try {
      const response = await fetchLibraryConfig();
      catalog = response.settings;
      roots = response.roots;
      savedMetadataStorageDedicated = effectiveMetadataStorageValue();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load settings");
    }
  }

  async function loadSecurity() {
    securityBusy = true;
    try {
      const [key, profiles] = await Promise.all([
        fetchApiKey(),
        fetchJellyfinProfiles(),
      ]);
      apiKey = key;
      jellyfinProfiles = profiles;
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load API access settings");
    } finally {
      securityBusy = false;
    }
  }

  async function copyApiKey() {
    if (!apiKey) return;
    try {
      await navigator.clipboard.writeText(apiKey.apiKey);
      flashMessage("API key copied.");
    } catch {
      setError("Could not copy the API key.");
    }
  }

  async function handleRegenerateApiKey() {
    if (!window.confirm("Regenerate the API key? Existing Jellyfin app sessions will need to sign in again.")) {
      return;
    }

    securityBusy = true;
    setError(null);
    try {
      const regenerated = await regenerateApiKey();
      apiKey = regenerated;
      apiKeyRevealed = true;
      flashMessage(
        regenerated.invalidatedSessions > 0
          ? `API key regenerated. ${regenerated.invalidatedSessions} Jellyfin session(s) signed out.`
          : "API key regenerated.",
        4500,
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to regenerate API key");
    } finally {
      securityBusy = false;
    }
  }

  async function createProfile() {
    const username = profileUsername.trim();
    if (!username) {
      setError("Username is required.");
      return;
    }

    profileBusy = true;
    setError(null);
    try {
      const created = await createJellyfinProfile({
        username,
        displayName: profileDisplayName.trim() || null,
        allowSfw: profileAllowSfw,
        allowNsfw: profileAllowNsfw,
        enabled: true,
      });
      jellyfinProfiles = [...jellyfinProfiles, created].sort((a, b) =>
        a.username.localeCompare(b.username),
      );
      profileUsername = "";
      profileDisplayName = "";
      profileAllowSfw = true;
      profileAllowNsfw = false;
      flashMessage("Jellyfin profile created.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create Jellyfin profile");
    } finally {
      profileBusy = false;
    }
  }

  async function patchProfile(id: string, patch: Parameters<typeof updateJellyfinProfile>[1]) {
    profileBusy = true;
    setError(null);
    try {
      const updated = await updateJellyfinProfile(id, patch);
      jellyfinProfiles = jellyfinProfiles.map((profile) =>
        profile.id === id ? updated : profile,
      );
      flashMessage("Jellyfin profile saved.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to update Jellyfin profile");
      await loadSecurity();
    } finally {
      profileBusy = false;
    }
  }

  async function removeProfile(profile: JellyfinProfile) {
    if (!window.confirm(`Delete Jellyfin profile "${profile.username}"?`)) {
      return;
    }

    profileBusy = true;
    setError(null);
    try {
      await deleteJellyfinProfile(profile.id);
      jellyfinProfiles = jellyfinProfiles.filter((item) => item.id !== profile.id);
      flashMessage("Jellyfin profile deleted.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to delete Jellyfin profile");
    } finally {
      profileBusy = false;
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

  <!-- ── Content Visibility ── -->
  <Panel>
    <div class="p-5 space-y-5">
      <div class="flex items-center gap-2.5">
        <Eye class="h-4 w-4 text-text-accent" />
        <div>
          <h2 class="text-kicker text-text-primary">Content Visibility</h2>
          <p class="text-[0.68rem] text-text-muted">
            {findSettingsGroup(catalog, "visibility")?.description ??
              "Control how adult content is displayed across the application"}
          </p>
        </div>
      </div>

      <div class="grid gap-5 md:grid-cols-2">
        <!-- Device-local toggle (expressive) -->
        <div class="surface-well p-4 space-y-3">
          <div>
            <div class="text-label text-text-primary">This device</div>
            <p class="text-[0.68rem] text-text-muted">
              Stored in this browser. Does not affect stored data.
            </p>
          </div>

          <div class="flex rounded-sm border border-border-default bg-surface-1 p-1 shadow-well">
            <button
              type="button"
              onclick={() => nsfw.setMode("off")}
              class={cn(
                "flex flex-1 flex-col items-center justify-center gap-1.5 rounded-xs border py-2.5 transition-all duration-fast",
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
                "flex flex-1 flex-col items-center justify-center gap-1.5 rounded-xs border py-2.5 transition-all duration-fast",
                nsfw.mode === "show"
                  ? "border-border-accent bg-surface-3 text-accent-400 shadow-[var(--shadow-glow-accent)]"
                  : "border-transparent text-text-muted hover:bg-surface-2/50 hover:text-text-primary",
              )}
            >
              <Flame class={cn("h-4 w-4", nsfw.mode === "show" && "text-accent-500")} />
              <span class="text-[0.75rem] font-medium">Show</span>
            </button>
          </div>

          <p class="text-[0.68rem] text-text-disabled">
            {#if nsfw.mode === "off"}
              Adult content is hidden on this device.
            {:else}
              All content is displayed on this device.
            {/if}
          </p>
        </div>

        <!-- Server defaults (list rows) -->
        <div class="surface-well divide-y divide-border-subtle px-4">
          {#each settingsInGroup(catalog, "visibility") as setting (setting.key)}
            <SettingsControl {setting} onCommit={handleSettingCommit} />
          {/each}
        </div>
      </div>
    </div>
  </Panel>

  <!-- ── API Access ── -->
  <Panel>
    <div class="p-5 space-y-5">
      <div class="flex items-center gap-2.5">
        <KeyRound class="h-4 w-4 text-text-accent" />
        <div>
          <h2 class="text-kicker text-text-primary">API Access</h2>
          <p class="text-[0.68rem] text-text-muted">
            Jellyfin-compatible clients connect with the server key, then sign in as a profile.
          </p>
        </div>
      </div>

      <div class="grid gap-5 lg:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
        <!-- Server key -->
        <div class="surface-well flex flex-col gap-4 p-4">
          <div class="space-y-1.5">
            <div class="text-label text-text-muted">Server key</div>
            <div class="flex min-w-0 items-center gap-2">
              <code class="min-w-0 flex-1 overflow-hidden text-ellipsis rounded-xs border border-border-subtle bg-surface-1 px-3 py-2 font-mono text-[0.8rem] tracking-wide text-text-primary shadow-[inset_0_1px_3px_rgba(0,0,0,0.25)]">
                {apiKey
                  ? apiKeyRevealed
                    ? apiKey.apiKey
                    : "•".repeat(Math.min(apiKey.apiKey.length, 40))
                  : "Loading…"}
              </code>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                disabled={!apiKey || securityBusy}
                onclick={() => (apiKeyRevealed = !apiKeyRevealed)}
                aria-label={apiKeyRevealed ? "Hide server key" : "Show server key"}
              >
                {#if apiKeyRevealed}
                  <EyeOff class="h-4 w-4" />
                {:else}
                  <Eye class="h-4 w-4" />
                {/if}
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                disabled={!apiKey || securityBusy}
                onclick={() => void copyApiKey()}
                aria-label="Copy server key"
              >
                <Clipboard class="h-4 w-4" />
              </Button>
            </div>
            <p class="text-[0.68rem] leading-relaxed text-text-muted">
              Shared across all clients. Regenerating signs out every connected device.
            </p>
          </div>

          <Button
            type="button"
            variant="secondary"
            disabled={securityBusy}
            onclick={() => void handleRegenerateApiKey()}
            class="mt-auto w-full gap-2"
          >
            {#if securityBusy}
              <Loader2 class="h-4 w-4 animate-spin" />
            {:else}
              <RefreshCw class="h-4 w-4" />
            {/if}
            Regenerate key
          </Button>
        </div>

        <!-- Profiles -->
        <div class="space-y-3">
          <form
            class="surface-well space-y-3 p-4"
            onsubmit={(event) => {
              event.preventDefault();
              void createProfile();
            }}
          >
            <div class="grid gap-3 sm:grid-cols-2">
              <label class="space-y-1">
                <span class="text-label text-text-muted">Username</span>
                <TextInput
                  size="sm"
                  value={profileUsername}
                  oninput={(event) => (profileUsername = event.currentTarget.value)}
                  autocomplete="off"
                  placeholder="jellyfin-user"
                  disabled={profileBusy}
                />
              </label>
              <label class="space-y-1">
                <span class="text-label text-text-muted">Display name</span>
                <TextInput
                  size="sm"
                  value={profileDisplayName}
                  oninput={(event) => (profileDisplayName = event.currentTarget.value)}
                  autocomplete="off"
                  placeholder="Shown in clients"
                  disabled={profileBusy}
                />
              </label>
            </div>
            <div class="flex flex-wrap items-center justify-between gap-3">
              <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
                <label class="flex cursor-pointer items-center gap-2 text-[0.78rem] text-text-secondary">
                  <Checkbox
                    checked={profileAllowSfw}
                    disabled={profileBusy}
                    onchange={(event) =>
                      (profileAllowSfw = (event.currentTarget as HTMLInputElement).checked)}
                  />
                  Allow SFW content
                </label>
                <label class="flex cursor-pointer items-center gap-2 text-[0.78rem] text-text-secondary">
                  <Checkbox
                    checked={profileAllowNsfw}
                    disabled={profileBusy}
                    onchange={(event) =>
                      (profileAllowNsfw = (event.currentTarget as HTMLInputElement).checked)}
                  />
                  Allow NSFW content
                </label>
              </div>
              <Button
                type="submit"
                variant="primary"
                size="sm"
                disabled={profileBusy || !profileUsername.trim()}
                class="gap-2"
              >
                <UserPlus class="h-4 w-4" />
                Add profile
              </Button>
            </div>
          </form>

          <div class="surface-well divide-y divide-border-subtle px-4">
            {#each jellyfinProfiles as profile (profile.id)}
              <div
                class={cn(
                  "grid gap-3 py-3 transition-opacity",
                  !profile.enabled && "opacity-55",
                )}
              >
                <div class="grid gap-2 sm:grid-cols-[auto_minmax(0,1fr)] sm:items-start">
                  <StatusLed status={profile.enabled ? "active" : "idle"} size="sm" />
                  <div class="min-w-0 space-y-1.5">
                    <span class="block truncate text-[0.82rem] font-medium leading-snug text-text-primary">
                      {profile.displayName}
                    </span>
                    <div class="flex min-w-0 flex-col gap-1 sm:flex-row sm:flex-wrap sm:items-center sm:gap-x-2">
                      <span class="truncate font-mono text-[0.68rem] text-text-muted">
                        {profile.username}
                      </span>
                      {#if profile.allowSfw && profile.allowNsfw}
                        <Badge variant="warning">SFW + NSFW</Badge>
                      {:else if profile.allowSfw}
                        <Badge>SFW</Badge>
                      {:else if profile.allowNsfw}
                        <Badge variant="warning">NSFW</Badge>
                      {:else}
                        <Badge variant="error">No content</Badge>
                      {/if}
                    </div>
                  </div>
                </div>

                <div class="grid gap-2 md:grid-cols-3">
                  <div class="flex min-h-11 items-center justify-between gap-4 rounded-xs border border-border-subtle bg-surface-1/60 px-3 py-2">
                    <span class="text-label whitespace-nowrap text-text-muted">SFW</span>
                    <Toggle
                      size="sm"
                      checked={profile.allowSfw}
                      disabled={profileBusy}
                      ariaLabel={`Allow SFW for ${profile.username}`}
                      onchange={(checked) => void patchProfile(profile.id, { allowSfw: checked })}
                    />
                  </div>
                  <div class="flex min-h-11 items-center justify-between gap-4 rounded-xs border border-border-subtle bg-surface-1/60 px-3 py-2">
                    <span class="text-label whitespace-nowrap text-text-muted">NSFW</span>
                    <Toggle
                      size="sm"
                      checked={profile.allowNsfw}
                      disabled={profileBusy}
                      ariaLabel={`Allow NSFW for ${profile.username}`}
                      onchange={(checked) => void patchProfile(profile.id, { allowNsfw: checked })}
                    />
                  </div>
                  <div class="flex min-h-11 items-center justify-between gap-4 rounded-xs border border-border-subtle bg-surface-1/60 px-3 py-2">
                    <span class="text-label whitespace-nowrap text-text-muted">Enabled</span>
                    <Toggle
                      size="sm"
                      checked={profile.enabled}
                      disabled={profileBusy}
                      ariaLabel={`Enable ${profile.username}`}
                      onchange={(checked) => void patchProfile(profile.id, { enabled: checked })}
                    />
                  </div>
                </div>

                <div class="flex justify-end">
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    disabled={profileBusy}
                    onclick={() => void removeProfile(profile)}
                    aria-label={`Delete ${profile.username}`}
                    class="text-status-error-text hover:bg-error-muted/20"
                  >
                    <Trash2 class="h-4 w-4" />
                  </Button>
                </div>
              </div>
            {:else}
              <div class="flex flex-col items-center gap-1 py-8 text-center">
                <UserPlus class="h-5 w-5 text-text-disabled" />
                <p class="text-[0.78rem] font-medium text-text-secondary">No profiles yet</p>
                <p class="text-[0.68rem] text-text-muted">
                  Add a profile above so a client can sign in.
                </p>
              </div>
            {/each}
          </div>
        </div>
      </div>
    </div>
  </Panel>

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

  <!-- ── Diagnostics ── -->
  <DiagnosticsSection />
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
