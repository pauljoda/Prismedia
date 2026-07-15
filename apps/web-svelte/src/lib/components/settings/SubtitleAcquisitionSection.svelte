<script lang="ts">
  import { onMount } from "svelte";
  import {
    CheckCircle2,
    CloudDownload,
    ExternalLink,
    KeyRound,
    Loader2,
    PlugZap,
  } from "@lucide/svelte";
  import { Badge, Button, Panel, TextInput, Toggle } from "@prismedia/ui-svelte";
  import {
    fetchOpenSubtitlesConfiguration,
    saveOpenSubtitlesConfiguration,
    testOpenSubtitlesConnection,
    type OpenSubtitlesConfiguration,
    type SubtitleProviderTest,
  } from "$lib/api/subtitles";
  import type {
    SettingsCatalogResponse,
    SettingValue,
  } from "$lib/api/settings";
  import SettingsControl from "$lib/components/settings/SettingsControl.svelte";
  import {
    findSetting,
    settingKeys,
    valueAsBoolean,
  } from "$lib/settings/app-settings";

  interface Props {
    catalog: SettingsCatalogResponse | null;
    onCommit: (key: string, value: SettingValue) => void;
    onError: (message: string) => void;
    onMessage: (message: string) => void;
  }

  let { catalog, onCommit, onError, onMessage }: Props = $props();

  let configuration = $state.raw<OpenSubtitlesConfiguration | null>(null);
  let loading = $state(true);
  let saving = $state(false);
  let testing = $state(false);
  let enabled = $state(false);
  let includeAiTranslated = $state(false);
  let includeMachineTranslated = $state(false);
  let apiKey = $state("");
  let username = $state("");
  let password = $state("");
  let testResult = $state.raw<SubtitleProviderTest | null>(null);

  const automaticSettings = $derived.by(() => {
    const keys = [
      settingKeys.subtitlesAutoDownloadEnabled,
      settingKeys.subtitlesAutoDownloadLanguages,
      settingKeys.subtitlesAutoDownloadMinimumConfidence,
    ];
    return keys
      .map((key) => findSetting(catalog, key))
      .filter((setting) => setting !== null);
  });
  const autoDownloadEnabled = $derived(
    valueAsBoolean(findSetting(catalog, settingKeys.subtitlesAutoDownloadEnabled)?.value),
  );
  const hasUnsavedChanges = $derived(
    configuration !== null && (
      enabled !== configuration.enabled ||
      includeAiTranslated !== configuration.includeAiTranslated ||
      includeMachineTranslated !== configuration.includeMachineTranslated ||
      apiKey.trim().length > 0 ||
      username.trim().length > 0 ||
      password.length > 0
    ),
  );

  onMount(() => void loadConfiguration());

  function applyConfiguration(next: OpenSubtitlesConfiguration) {
    configuration = next;
    enabled = next.enabled;
    includeAiTranslated = next.includeAiTranslated;
    includeMachineTranslated = next.includeMachineTranslated;
    apiKey = "";
    username = "";
    password = "";
    testResult = null;
  }

  async function loadConfiguration() {
    loading = true;
    try {
      applyConfiguration(await fetchOpenSubtitlesConfiguration());
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to load OpenSubtitles configuration");
    } finally {
      loading = false;
    }
  }

  async function save() {
    if (saving) return;
    saving = true;
    testResult = null;
    try {
      const saved = await saveOpenSubtitlesConfiguration({
        enabled,
        apiKey: apiKey.trim() || null,
        username: username.trim() || null,
        password: password || null,
        includeAiTranslated,
        includeMachineTranslated,
      });
      applyConfiguration(saved);
      onMessage("OpenSubtitles configuration saved.");
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to save OpenSubtitles configuration");
    } finally {
      saving = false;
    }
  }

  async function testConnection() {
    if (testing || hasUnsavedChanges) return;
    testing = true;
    testResult = null;
    try {
      testResult = await testOpenSubtitlesConnection();
    } catch (err) {
      testResult = {
        success: false,
        message: err instanceof Error ? err.message : "OpenSubtitles connection test failed.",
      };
    } finally {
      testing = false;
    }
  }

  function credentialPlaceholder(configured: boolean, label: string): string {
    return configured ? `${label} configured — leave blank to keep` : label;
  }
</script>

<Panel>
  <div class="space-y-5 p-5">
    <div class="flex items-start gap-2.5">
      <CloudDownload class="mt-0.5 h-4 w-4 text-text-accent" />
      <div>
        <h2 class="text-kicker text-text-primary">Automatic acquisition</h2>
        <p class="text-[0.68rem] leading-relaxed text-text-muted">
          Search after local subtitle reconciliation and acquire only strong identity matches.
          Manual search remains available from each video transcript.
        </p>
      </div>
    </div>

    <div class="divide-y divide-border-subtle px-1">
      {#each automaticSettings as setting (setting.key)}
        <SettingsControl {setting} {onCommit} />
      {/each}
    </div>

    {#if autoDownloadEnabled}
      <div class="border-l-2 border-status-warning bg-warning-muted/15 px-3 py-2 text-[0.68rem] leading-relaxed text-status-warning-text">
        Automatic acquisition only accepts exact file hashes or strong episode identity at the
        configured confidence. Ambiguous candidates stay available for manual review.
      </div>
    {/if}
  </div>
</Panel>

<Panel>
  <div class="space-y-5 p-5">
    <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
      <div class="flex items-start gap-2.5">
        <PlugZap class="mt-0.5 h-4 w-4 text-text-accent" />
        <div>
          <div class="flex flex-wrap items-center gap-2">
            <h2 class="text-kicker text-text-primary">OpenSubtitles.com</h2>
            {#if configuration}
              <Badge variant={configuration.enabled ? "success" : "default"}>
                {configuration.enabled ? "Enabled" : "Disabled"}
              </Badge>
            {/if}
          </div>
          <p class="mt-1 max-w-2xl text-[0.68rem] leading-relaxed text-text-muted">
            OpenSubtitles supplies hash-aware and metadata-aware subtitle matches. Credentials are
            stored server-side and are never returned to this screen.
          </p>
        </div>
      </div>
      <a
        href="https://www.opensubtitles.com/en/consumers"
        target="_blank"
        rel="noreferrer"
        class="inline-flex shrink-0 items-center gap-1 text-[0.68rem] text-text-accent hover:text-text-accent-bright"
      >
        API consumers
        <ExternalLink class="h-3 w-3" />
      </a>
    </div>

    {#if loading}
      <div class="flex items-center justify-center py-8 text-[0.74rem] text-text-muted">
        <Loader2 class="mr-2 h-4 w-4 animate-spin" />
        Loading provider configuration…
      </div>
    {:else if configuration}
      <div class="surface-well space-y-4 p-4">
        <div class="flex items-center justify-between gap-4">
          <div>
            <div class="text-[0.8rem] font-medium text-text-primary">Use OpenSubtitles</div>
            <p class="mt-0.5 text-[0.66rem] text-text-muted">
              Makes the provider available to manual and automatic subtitle searches.
            </p>
          </div>
          <Toggle
            checked={enabled}
            onchange={(checked) => (enabled = checked)}
            ariaLabel="Enable OpenSubtitles"
          />
        </div>

        <div class="grid gap-3 lg:grid-cols-3">
          <label class="space-y-1.5">
            <span class="flex items-center gap-1.5 text-label text-text-muted">
              <KeyRound class="h-3 w-3" />
              API key
              {#if configuration.apiKeyConfigured}<Badge variant="success">Configured</Badge>{/if}
            </span>
            <TextInput
              type="password"
              value={apiKey}
              oninput={(event) => (apiKey = event.currentTarget.value)}
              autocomplete="off"
              placeholder={credentialPlaceholder(configuration.apiKeyConfigured, "OpenSubtitles API key")}
            />
          </label>
          <label class="space-y-1.5">
            <span class="text-label text-text-muted">
              Username
              {#if configuration.usernameConfigured}<Badge variant="success">Configured</Badge>{/if}
            </span>
            <TextInput
              value={username}
              oninput={(event) => (username = event.currentTarget.value)}
              autocomplete="off"
              placeholder={credentialPlaceholder(configuration.usernameConfigured, "OpenSubtitles username")}
            />
          </label>
          <label class="space-y-1.5">
            <span class="text-label text-text-muted">
              Password
              {#if configuration.passwordConfigured}<Badge variant="success">Configured</Badge>{/if}
            </span>
            <TextInput
              type="password"
              value={password}
              oninput={(event) => (password = event.currentTarget.value)}
              autocomplete="new-password"
              placeholder={credentialPlaceholder(configuration.passwordConfigured, "OpenSubtitles password")}
            />
          </label>
        </div>

        <div class="grid gap-3 md:grid-cols-2">
          <div class="flex items-center justify-between gap-3 border border-border-subtle bg-surface-1/70 px-3 py-2.5">
            <div>
              <div class="text-[0.75rem] text-text-primary">Include AI translations</div>
              <p class="mt-0.5 text-[0.62rem] text-text-muted">Shown with a quality penalty and clear label.</p>
            </div>
            <Toggle
              checked={includeAiTranslated}
              onchange={(checked) => (includeAiTranslated = checked)}
              ariaLabel="Include AI-translated subtitles"
            />
          </div>
          <div class="flex items-center justify-between gap-3 border border-border-subtle bg-surface-1/70 px-3 py-2.5">
            <div>
              <div class="text-[0.75rem] text-text-primary">Include machine translations</div>
              <p class="mt-0.5 text-[0.62rem] text-text-muted">Shown with a stronger quality penalty.</p>
            </div>
            <Toggle
              checked={includeMachineTranslated}
              onchange={(checked) => (includeMachineTranslated = checked)}
              ariaLabel="Include machine-translated subtitles"
            />
          </div>
        </div>

        <div class="flex flex-col gap-3 border-t border-border-subtle pt-4 sm:flex-row sm:items-center sm:justify-between">
          <div class="min-h-5 text-[0.68rem]">
            {#if testResult}
              <span class={testResult.success ? "inline-flex items-center gap-1.5 text-status-success-text" : "text-status-error-text"}>
                {#if testResult.success}<CheckCircle2 class="h-3.5 w-3.5" />{/if}
                {testResult.message}
              </span>
            {:else if hasUnsavedChanges}
              <span class="text-text-disabled">Save changes before testing the connection.</span>
            {/if}
          </div>
          <div class="flex gap-2">
            <Button
              variant="secondary"
              disabled={testing || hasUnsavedChanges}
              onclick={() => void testConnection()}
              class="no-lift"
            >
              {#if testing}<Loader2 class="h-3.5 w-3.5 animate-spin" />{/if}
              Test connection
            </Button>
            <Button
              disabled={saving || !hasUnsavedChanges}
              onclick={() => void save()}
              class="no-lift"
            >
              {#if saving}<Loader2 class="h-3.5 w-3.5 animate-spin" />{/if}
              Save provider
            </Button>
          </div>
        </div>
      </div>
    {/if}
  </div>
</Panel>
