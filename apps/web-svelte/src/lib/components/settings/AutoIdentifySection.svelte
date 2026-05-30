<script lang="ts">
  import { onMount } from "svelte";
  import {
    AlertTriangle,
    ArrowDown,
    ArrowUp,
    Puzzle,
    Search,
    Sparkles,
  } from "@lucide/svelte";
  import { Badge, Checkbox, Panel, TextInput, Toggle, cn } from "@prismedia/ui-svelte";
  import SettingsControl from "$lib/components/settings/SettingsControl.svelte";
  import {
    findSetting,
    settingKeys,
    valueAsBoolean,
    valueAsStringList,
  } from "$lib/settings/app-settings";
  import type {
    SettingsCatalogResponse,
    SettingValue,
  } from "$lib/api/settings";
  import { fetchPluginProviders, type PluginProvider } from "$lib/api/plugins";

  interface Props {
    catalog: SettingsCatalogResponse | null;
    onCommit: (key: string, value: SettingValue) => void;
  }

  let { catalog, onCommit }: Props = $props();

  // High-level media kinds auto identify can target. Codes match the backend selector kinds.
  const ENTITY_KINDS: ReadonlyArray<{ key: string; label: string }> = [
    { key: "video", label: "Videos" },
    { key: "gallery", label: "Galleries" },
    { key: "image", label: "Images" },
    { key: "audio", label: "Audio" },
    { key: "book", label: "Books" },
  ];

  let providers = $state<PluginProvider[]>([]);
  let loadingProviders = $state(true);
  let providerError = $state<string | null>(null);
  let search = $state("");

  const enabledSetting = $derived(findSetting(catalog, settingKeys.autoIdentifyEnabled));
  const confidenceSetting = $derived(findSetting(catalog, settingKeys.autoIdentifyConfidenceThreshold));
  const unorganizedSetting = $derived(findSetting(catalog, settingKeys.autoIdentifyUnorganizedOnly));

  const enabled = $derived(valueAsBoolean(enabledSetting?.value));
  const selectedProviders = $derived(
    valueAsStringList(findSetting(catalog, settingKeys.autoIdentifyProviders)?.value),
  );
  const selectedKinds = $derived(
    valueAsStringList(findSetting(catalog, settingKeys.autoIdentifyEntityKinds)?.value),
  );

  // Only installed and enabled providers are eligible for auto identify.
  const installed = $derived(providers.filter((p) => p.installed && p.enabled));
  const hasPlugins = $derived(installed.length > 0);
  const masterToggleDisabled = $derived(!hasPlugins && !enabled);

  const searchTerm = $derived(search.trim().toLowerCase());
  // Enabled providers float to the top in priority order; the rest follow alphabetically.
  const orderedProviders = $derived(
    [...installed]
      .filter(
        (p) =>
          !searchTerm ||
          p.name.toLowerCase().includes(searchTerm) ||
          p.id.toLowerCase().includes(searchTerm),
      )
      .sort((a, b) => {
        const ia = selectedProviders.indexOf(a.id);
        const ib = selectedProviders.indexOf(b.id);
        if (ia !== -1 && ib !== -1) return ia - ib;
        if (ia !== -1) return -1;
        if (ib !== -1) return 1;
        return a.name.localeCompare(b.name);
      }),
  );

  onMount(() => {
    void loadProviders();
  });

  async function loadProviders() {
    loadingProviders = true;
    try {
      providers = await fetchPluginProviders();
      providerError = null;
    } catch (err) {
      providerError = err instanceof Error ? err.message : "Failed to load plugins";
    } finally {
      loadingProviders = false;
    }
  }

  function priorityOf(id: string): number {
    return selectedProviders.indexOf(id);
  }

  function toggleProvider(id: string) {
    const next = selectedProviders.includes(id)
      ? selectedProviders.filter((value) => value !== id)
      : [...selectedProviders, id];
    onCommit(settingKeys.autoIdentifyProviders, next);
  }

  function moveProvider(id: string, direction: -1 | 1) {
    const index = selectedProviders.indexOf(id);
    if (index === -1) return;
    const target = index + direction;
    if (target < 0 || target >= selectedProviders.length) return;
    const next = [...selectedProviders];
    [next[index], next[target]] = [next[target], next[index]];
    onCommit(settingKeys.autoIdentifyProviders, next);
  }

  function toggleKind(key: string) {
    const next = selectedKinds.includes(key)
      ? selectedKinds.filter((value) => value !== key)
      : [...selectedKinds, key];
    onCommit(settingKeys.autoIdentifyEntityKinds, next);
  }

  function supportedKindLabels(provider: PluginProvider): string[] {
    return (provider.supports ?? [])
      .map((support) => support.entityKind)
      .filter((kind, i, arr) => arr.indexOf(kind) === i);
  }
</script>

<Panel>
  <div class="p-5 space-y-5">
    <div class="flex items-center gap-2.5">
      <Sparkles class="h-4 w-4 text-text-accent" />
      <div>
        <h2 class="text-kicker text-text-primary">Auto Identify</h2>
        <p class="text-[0.68rem] text-text-muted">
          Let trusted plugins identify and fill new media automatically during scans
        </p>
      </div>
    </div>

    <!-- Master toggle -->
    <div class="surface-well p-4">
      <div class="flex items-center justify-between gap-4">
        <div class="min-w-0 flex-1">
          <div class="text-[0.82rem] font-medium text-text-primary">
            {enabledSetting?.label ?? "Auto identify during scans"}
          </div>
          <p class="mt-0.5 text-[0.68rem] leading-relaxed text-text-muted">
            When on, each scanned item runs through your enabled plugins and the first confident
            match is applied automatically — no manual review needed.
          </p>
          <p class="mt-1.5 flex items-center gap-1.5 text-[0.66rem] text-status-warning-text">
            <AlertTriangle class="h-3 w-3 shrink-0" />
            Identifying every item can noticeably increase library scan times.
          </p>
          {#if !hasPlugins && !loadingProviders}
            <p class="mt-1 flex items-center gap-1.5 text-[0.66rem] text-text-disabled">
              <Puzzle class="h-3 w-3 shrink-0" />
              Install at least one plugin to enable auto identify.
            </p>
          {/if}
        </div>
        <Toggle
          checked={enabled}
          disabled={masterToggleDisabled}
          onchange={(checked) => onCommit(settingKeys.autoIdentifyEnabled, checked)}
        />
      </div>
    </div>

    {#if enabled}
      <!-- Confidence + scope -->
      <div class="divide-y divide-border-subtle px-1">
        {#if confidenceSetting}
          <SettingsControl setting={confidenceSetting} {onCommit} />
        {/if}
        {#if unorganizedSetting}
          <SettingsControl setting={unorganizedSetting} {onCommit} />
        {/if}
      </div>

      <!-- Entity kinds -->
      <div class="space-y-2">
        <div class="text-label text-text-muted">Identify these kinds</div>
        <div class="flex flex-wrap gap-2">
          {#each ENTITY_KINDS as kind (kind.key)}
            {@const active = selectedKinds.includes(kind.key)}
            <button
              type="button"
              onclick={() => toggleKind(kind.key)}
              class={cn(
                "rounded-sm border px-3 py-1.5 text-[0.74rem] font-medium transition-all duration-fast",
                active
                  ? "border-border-accent bg-surface-3 text-accent-400 shadow-[var(--shadow-glow-accent)]"
                  : "border-border-default bg-surface-1 text-text-muted hover:border-border-subtle hover:bg-surface-2/60 hover:text-text-primary",
              )}
            >
              {kind.label}
            </button>
          {/each}
        </div>
      </div>

      <!-- Provider checklist -->
      <div class="space-y-2.5">
        <div class="flex items-center justify-between gap-3">
          <div class="text-label text-text-muted">
            Enabled plugins
            {#if selectedProviders.length > 0}
              <span class="text-text-disabled">· tried in order</span>
            {/if}
          </div>
        </div>

        {#if loadingProviders}
          <p class="text-[0.7rem] text-text-muted">Loading plugins…</p>
        {:else if providerError}
          <p class="text-[0.7rem] text-status-error-text">{providerError}</p>
        {:else if !hasPlugins}
          <p class="text-[0.7rem] text-text-muted">
            No installed plugins yet. Add one from the Plugins page first.
          </p>
        {:else}
          <div class="relative">
            <Search
              class="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-text-disabled"
            />
            <TextInput
              value={search}
              oninput={(e) => (search = (e.currentTarget as HTMLInputElement).value)}
              size="sm"
              placeholder="Search plugins…"
              class="pl-8"
              aria-label="Search plugins"
            />
          </div>

          <div class="surface-well divide-y divide-border-subtle overflow-hidden">
            {#each orderedProviders as provider (provider.id)}
              {@const checked = selectedProviders.includes(provider.id)}
              {@const priority = priorityOf(provider.id)}
              <div class="flex items-center gap-3 px-3 py-2.5">
                <Checkbox {checked} onchange={() => toggleProvider(provider.id)} />
                <button
                  type="button"
                  onclick={() => toggleProvider(provider.id)}
                  class="min-w-0 flex-1 text-left"
                >
                  <div class="flex items-center gap-2">
                    {#if checked}
                      <span
                        class="rounded-xs border border-border-accent/40 bg-surface-3 px-1.5 text-mono-sm text-text-accent"
                      >
                        {priority + 1}
                      </span>
                    {/if}
                    <span class="truncate text-[0.78rem] font-medium text-text-primary">
                      {provider.name}
                    </span>
                    <span class="text-mono-sm text-text-disabled">v{provider.version}</span>
                  </div>
                  <div class="mt-1 flex flex-wrap gap-1">
                    {#each supportedKindLabels(provider) as kind (kind)}
                      <Badge variant="default" class="text-[0.6rem]">{kind}</Badge>
                    {/each}
                  </div>
                </button>
                {#if checked}
                  <div class="flex shrink-0 flex-col">
                    <button
                      type="button"
                      onclick={() => moveProvider(provider.id, -1)}
                      disabled={priority <= 0}
                      class="px-1 text-text-muted transition-colors hover:text-text-primary disabled:opacity-30"
                      aria-label="Increase priority"
                    >
                      <ArrowUp class="h-3.5 w-3.5" />
                    </button>
                    <button
                      type="button"
                      onclick={() => moveProvider(provider.id, 1)}
                      disabled={priority >= selectedProviders.length - 1}
                      class="px-1 text-text-muted transition-colors hover:text-text-primary disabled:opacity-30"
                      aria-label="Decrease priority"
                    >
                      <ArrowDown class="h-3.5 w-3.5" />
                    </button>
                  </div>
                {/if}
              </div>
            {/each}
          </div>
        {/if}
      </div>
    {/if}
  </div>
</Panel>
