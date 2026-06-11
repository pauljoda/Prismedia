<script lang="ts">
  import { onMount } from "svelte";
  import { CloudDownload, Loader2, Pencil, PlugZap, Plus, Trash2 } from "@lucide/svelte";
  import { Badge, Button, Checkbox, Panel, Select, StatusLed, TextInput, cn } from "@prismedia/ui-svelte";
  import { REQUEST_MINIMUM_AVAILABILITY, REQUEST_PROVIDER_KIND } from "$lib/api/generated/codes";
  import type { RequestMinimumAvailabilityCode, RequestProviderKindCode } from "$lib/api/generated/codes";
  import {
    deleteRequestServiceInstance,
    fetchRequestServices,
    saveRequestServiceInstance,
    testRequestServiceConnection,
  } from "$lib/api/requests";
  import type {
    RequestServiceInstanceSaveRequest,
    RequestServiceInstanceSummary,
    RequestServiceOptionsResponse,
    RequestServiceTestResponse,
  } from "$lib/requests/request-model";
  import { numericValue } from "$lib/requests/request-helpers";

  interface Props {
    onError: (msg: string) => void;
    onMessage: (msg: string) => void;
  }

  let { onError, onMessage }: Props = $props();

  const providerOptions: { value: RequestProviderKindCode; label: string }[] = [
    { value: REQUEST_PROVIDER_KIND.radarr, label: "Radarr (movies)" },
    { value: REQUEST_PROVIDER_KIND.sonarr, label: "Sonarr (series)" },
    { value: REQUEST_PROVIDER_KIND.lidarr, label: "Lidarr (music)" },
  ];

  const providerLabels: Record<string, string> = {
    [REQUEST_PROVIDER_KIND.radarr]: "Radarr",
    [REQUEST_PROVIDER_KIND.sonarr]: "Sonarr",
    [REQUEST_PROVIDER_KIND.lidarr]: "Lidarr",
    [REQUEST_PROVIDER_KIND.plugin]: "Plugin",
  };

  const availabilityOptions: { value: RequestMinimumAvailabilityCode; label: string }[] = [
    { value: REQUEST_MINIMUM_AVAILABILITY.announced, label: "Announced" },
    { value: REQUEST_MINIMUM_AVAILABILITY.inCinemas, label: "In Cinemas" },
    { value: REQUEST_MINIMUM_AVAILABILITY.released, label: "Released" },
  ];

  let services = $state<RequestServiceInstanceSummary[]>([]);
  let formOpen = $state(false);
  let editingServiceId = $state<string | null>(null);
  let saving = $state(false);
  let testing = $state(false);
  let testMessage = $state<string | null>(null);
  let rowTestingId = $state<string | null>(null);
  let rowTestResults = $state<Record<string, { connected: boolean; message: string | null }>>({});
  // Options pulled from the service by the last successful test. Non-null means the
  // connection is verified and defaults + Save are unlocked.
  let verifiedOptions = $state<RequestServiceOptionsResponse | null>(null);
  let form = $state<RequestServiceInstanceSaveRequest>(emptyForm());

  const isLidarr = $derived(form.kind === REQUEST_PROVIDER_KIND.lidarr);
  const isRadarr = $derived(form.kind === REQUEST_PROVIDER_KIND.radarr);
  const connectionFilled = $derived(!!form.baseUrl.trim() && (!!form.apiKey?.trim() || !!editingServiceId));
  const canSave = $derived(!!verifiedOptions && !!form.displayName.trim() && !!form.baseUrl.trim());

  onMount(async () => {
    try {
      services = await fetchRequestServices();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to load request services");
    }
  });

  function emptyForm(): RequestServiceInstanceSaveRequest {
    return {
      id: null,
      kind: REQUEST_PROVIDER_KIND.radarr,
      displayName: "",
      baseUrl: "",
      apiKey: null,
      defaultRootFolderPath: null,
      defaultQualityProfileId: null,
      defaultMetadataProfileId: null,
      minimumAvailability: REQUEST_MINIMUM_AVAILABILITY.released,
      defaultTagIds: [],
      searchOnRequest: true,
      isDefault: false,
    };
  }

  function openNewForm() {
    formOpen = true;
    editingServiceId = null;
    form = emptyForm();
    invalidateTest();
  }

  function openEditForm(service: RequestServiceInstanceSummary) {
    formOpen = true;
    editingServiceId = service.id;
    form = {
      id: service.id,
      kind: service.kind,
      displayName: service.displayName,
      baseUrl: service.baseUrl,
      apiKey: null,
      defaultRootFolderPath: service.defaultRootFolderPath,
      defaultQualityProfileId: numericValue(service.defaultQualityProfileId),
      defaultMetadataProfileId: numericValue(service.defaultMetadataProfileId),
      minimumAvailability: service.minimumAvailability,
      defaultTagIds: service.defaultTagIds.map(numericValue).filter((id): id is number => id !== null),
      searchOnRequest: service.searchOnRequest,
      isDefault: service.isDefault,
    };
    invalidateTest();
  }

  function closeForm() {
    formOpen = false;
    editingServiceId = null;
    form = emptyForm();
    invalidateTest();
  }

  /** Connection fields changed — the previous test no longer vouches for this config. */
  function invalidateTest() {
    verifiedOptions = null;
    testMessage = null;
  }

  function setConnectionField(patch: Partial<RequestServiceInstanceSaveRequest>) {
    form = { ...form, ...patch };
    invalidateTest();
  }

  async function testConnection() {
    testing = true;
    testMessage = null;
    try {
      const response = await testRequestServiceConnection({
        id: editingServiceId,
        kind: form.kind,
        baseUrl: form.baseUrl.trim(),
        apiKey: form.apiKey?.trim() || null,
      });
      if (response.connected && response.options) {
        verifiedOptions = response.options;
        seedDefaults(response.options);
        testMessage = response.message ?? "Connected";
      } else {
        verifiedOptions = null;
        testMessage = response.message ?? "Connection failed";
      }
    } catch (err) {
      verifiedOptions = null;
      testMessage = err instanceof Error ? err.message : "Failed to test connection";
    } finally {
      testing = false;
    }
  }

  /** Keeps saved defaults when the service still offers them, otherwise falls back to the first option. */
  function seedDefaults(options: RequestServiceOptionsResponse) {
    const rootFolder =
      options.rootFolders.find((option) => option.path === form.defaultRootFolderPath) ??
      options.rootFolders[0];
    const qualityProfile =
      options.qualityProfiles.find(
        (option) => numericValue(option.id) === numericValue(form.defaultQualityProfileId),
      ) ?? options.qualityProfiles[0];
    const metadataProfile =
      options.metadataProfiles.find(
        (option) => numericValue(option.id) === numericValue(form.defaultMetadataProfileId),
      ) ?? options.metadataProfiles[0];
    const availableTagIds = new Set(
      options.tags.map((option) => numericValue(option.id)).filter((id) => id !== null),
    );
    form = {
      ...form,
      defaultRootFolderPath: rootFolder?.path ?? null,
      defaultQualityProfileId: numericValue(qualityProfile?.id),
      defaultMetadataProfileId: numericValue(metadataProfile?.id),
      defaultTagIds: form.defaultTagIds
        .map(numericValue)
        .filter((id): id is number => id !== null && availableTagIds.has(id)),
    };
  }

  function toggleTag(id: number) {
    const current = form.defaultTagIds.map(numericValue).filter((tag): tag is number => tag !== null);
    form = {
      ...form,
      defaultTagIds: current.includes(id)
        ? current.filter((tag) => tag !== id)
        : [...current, id],
    };
  }

  function tagSelected(id: string | number) {
    const numeric = numericValue(id);
    return numeric !== null && form.defaultTagIds.map(numericValue).includes(numeric);
  }

  async function saveService() {
    if (!canSave) return;
    saving = true;
    try {
      const saved = await saveRequestServiceInstance({
        ...form,
        displayName: form.displayName.trim(),
        baseUrl: form.baseUrl.trim(),
        apiKey: form.apiKey?.trim() || null,
        defaultRootFolderPath: form.defaultRootFolderPath?.trim() || null,
        defaultQualityProfileId: numericValue(form.defaultQualityProfileId),
        defaultMetadataProfileId: numericValue(form.defaultMetadataProfileId),
        defaultTagIds: form.defaultTagIds.map(numericValue).filter((id): id is number => id !== null),
      });
      services = await fetchRequestServices();
      onMessage(`${saved.displayName} saved.`);
      closeForm();
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to save request service");
    } finally {
      saving = false;
    }
  }

  async function deleteService(service: RequestServiceInstanceSummary) {
    if (!window.confirm(`Delete request service "${service.displayName}"?`)) return;
    try {
      await deleteRequestServiceInstance(service.id);
      services = await fetchRequestServices();
      if (editingServiceId === service.id) closeForm();
      onMessage(`${service.displayName} deleted.`);
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to delete request service");
    }
  }

  async function testServiceRow(service: RequestServiceInstanceSummary) {
    rowTestingId = service.id;
    try {
      const response: RequestServiceTestResponse = await testRequestServiceConnection({
        id: service.id,
        kind: service.kind,
        baseUrl: service.baseUrl,
        apiKey: null,
      });
      rowTestResults = {
        ...rowTestResults,
        [service.id]: {
          connected: response.connected,
          message: response.message ?? (response.connected ? "Connected" : "Connection failed"),
        },
      };
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to test request service");
    } finally {
      rowTestingId = null;
    }
  }
</script>

<Panel>
  <div class="p-5 space-y-5">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div class="flex items-center gap-2.5">
        <CloudDownload class="h-4 w-4 text-text-accent" />
        <div>
          <h2 class="text-kicker text-text-primary">Request Services</h2>
          <p class="text-[0.68rem] text-text-muted">
            Connect Radarr, Sonarr, and Lidarr instances that fulfill media requests
          </p>
        </div>
      </div>
      <Button
        type="button"
        variant="secondary"
        size="sm"
        onclick={openNewForm}
        class="no-lift gap-1.5 px-3 py-1.5 text-xs"
      >
        <Plus class="h-3.5 w-3.5" />
        Add Service
      </Button>
    </div>

    {#if formOpen}
      <form
        class="surface-well space-y-4 p-4"
        onsubmit={(event) => {
          event.preventDefault();
          void saveService();
        }}
      >
        <!-- ── Step 1: connection ── -->
        <div class="space-y-3">
          <div class="text-label text-text-muted">Connection</div>
          <div class="grid gap-3 sm:grid-cols-2">
            <label class="space-y-1">
              <span class="text-label text-text-muted">Provider</span>
              <Select
                size="sm"
                value={form.kind}
                options={providerOptions}
                disabled={!!editingServiceId}
                onchange={(value) => setConnectionField({ kind: value as RequestProviderKindCode })}
              />
            </label>
            <label class="space-y-1">
              <span class="text-label text-text-muted">Name</span>
              <TextInput
                size="sm"
                value={form.displayName}
                oninput={(event) => (form = { ...form, displayName: event.currentTarget.value })}
                placeholder="Movies"
                autocomplete="off"
                required
              />
            </label>
            <label class="space-y-1">
              <span class="text-label text-text-muted">Base URL</span>
              <TextInput
                size="sm"
                value={form.baseUrl}
                oninput={(event) => setConnectionField({ baseUrl: event.currentTarget.value })}
                placeholder="http://radarr:7878"
                autocomplete="off"
                required
              />
            </label>
            <label class="space-y-1">
              <span class="text-label text-text-muted">API key</span>
              <TextInput
                size="sm"
                type="password"
                value={form.apiKey ?? ""}
                oninput={(event) => setConnectionField({ apiKey: event.currentTarget.value })}
                placeholder={editingServiceId ? "Saved key kept unless replaced" : ""}
                autocomplete="new-password"
              />
            </label>
          </div>

          <div class="flex flex-wrap items-center gap-3">
            <Button
              type="button"
              variant={verifiedOptions ? "secondary" : "primary"}
              size="sm"
              disabled={testing || !connectionFilled}
              onclick={() => void testConnection()}
              class="gap-1.5 px-3 py-1.5 text-xs"
            >
              {#if testing}
                <Loader2 class="h-3.5 w-3.5 animate-spin" />
              {:else}
                <PlugZap class="h-3.5 w-3.5" />
              {/if}
              {testing ? "Testing…" : "Test Connection"}
            </Button>
            {#if testMessage}
              <span
                class={cn(
                  "flex items-center gap-1.5 text-[0.75rem]",
                  verifiedOptions ? "text-success-text" : "text-error-text",
                )}
              >
                <StatusLed status={verifiedOptions ? "active" : "error"} size="sm" />
                {testMessage}
              </span>
            {:else}
              <span class="text-[0.72rem] text-text-muted">
                Test the connection to load folders and profiles before saving.
              </span>
            {/if}
          </div>
        </div>

        <!-- ── Step 2: defaults (unlocked by a successful test) ── -->
        {#if verifiedOptions}
          <div class="space-y-3 border-t border-border-subtle pt-4">
            <div class="text-label text-text-muted">Request defaults</div>
            <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              <label class="space-y-1">
                <span class="text-label text-text-muted">Root folder</span>
                <Select
                  size="sm"
                  value={form.defaultRootFolderPath ?? ""}
                  options={verifiedOptions.rootFolders.map((option) => ({
                    value: option.path ?? option.id,
                    label: option.name,
                  }))}
                  disabled={verifiedOptions.rootFolders.length === 0}
                  onchange={(value) => (form = { ...form, defaultRootFolderPath: value })}
                />
              </label>
              <label class="space-y-1">
                <span class="text-label text-text-muted">Quality profile</span>
                <Select
                  size="sm"
                  value={form.defaultQualityProfileId === null
                    ? ""
                    : String(form.defaultQualityProfileId)}
                  options={verifiedOptions.qualityProfiles.map((option) => ({
                    value: String(option.id),
                    label: option.name,
                  }))}
                  disabled={verifiedOptions.qualityProfiles.length === 0}
                  onchange={(value) => (form = { ...form, defaultQualityProfileId: numericValue(value) })}
                />
              </label>
              {#if isLidarr}
                <label class="space-y-1">
                  <span class="text-label text-text-muted">Metadata profile</span>
                  <Select
                    size="sm"
                    value={form.defaultMetadataProfileId === null
                      ? ""
                      : String(form.defaultMetadataProfileId)}
                    options={verifiedOptions.metadataProfiles.map((option) => ({
                      value: String(option.id),
                      label: option.name,
                    }))}
                    disabled={verifiedOptions.metadataProfiles.length === 0}
                    onchange={(value) =>
                      (form = { ...form, defaultMetadataProfileId: numericValue(value) })}
                  />
                </label>
              {/if}
              {#if isRadarr}
                <label class="space-y-1">
                  <span class="text-label text-text-muted">Minimum availability</span>
                  <Select
                    size="sm"
                    value={form.minimumAvailability}
                    options={availabilityOptions}
                    onchange={(value) =>
                      (form = { ...form, minimumAvailability: value as RequestMinimumAvailabilityCode })}
                  />
                </label>
              {/if}
            </div>

            {#if verifiedOptions.tags.length > 0}
              <div class="space-y-1.5">
                <span class="text-label text-text-muted">Tags applied to requests</span>
                <div class="flex flex-wrap gap-1.5">
                  {#each verifiedOptions.tags as tag (tag.id)}
                    <button
                      type="button"
                      onclick={() => {
                        const id = numericValue(tag.id);
                        if (id !== null) toggleTag(id);
                      }}
                      class={cn(
                        "rounded-xs border px-2 py-1 text-[0.7rem] font-medium transition-all duration-fast",
                        tagSelected(tag.id)
                          ? "bg-accent-950/30 border-border-accent text-text-accent shadow-[var(--shadow-glow-accent)]"
                          : "bg-surface-1 border-border-subtle text-text-muted hover:border-border-default hover:text-text-primary",
                      )}
                    >
                      {tag.name}
                    </button>
                  {/each}
                </div>
              </div>
            {/if}

            <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
              <label class="flex cursor-pointer items-center gap-2 text-[0.78rem] text-text-secondary">
                <Checkbox
                  checked={form.searchOnRequest}
                  onchange={(event) =>
                    (form = { ...form, searchOnRequest: event.currentTarget.checked })}
                />
                Search after request
              </label>
              <label class="flex cursor-pointer items-center gap-2 text-[0.78rem] text-text-secondary">
                <Checkbox
                  checked={form.isDefault}
                  onchange={(event) => (form = { ...form, isDefault: event.currentTarget.checked })}
                />
                Default for this provider
              </label>
            </div>
          </div>
        {/if}

        <div class="flex items-center justify-end gap-2 border-t border-border-subtle pt-3">
          <Button type="button" variant="ghost" size="sm" onclick={closeForm} class="px-3 py-1.5 text-xs">
            Cancel
          </Button>
          <Button
            type="submit"
            variant="primary"
            size="sm"
            disabled={saving || !canSave}
            title={verifiedOptions ? undefined : "Run a successful connection test before saving"}
            class="gap-1.5 px-3 py-1.5 text-xs"
          >
            {#if saving}
              <Loader2 class="h-3.5 w-3.5 animate-spin" />
            {/if}
            {editingServiceId ? "Save Changes" : "Add Service"}
          </Button>
        </div>
      </form>
    {/if}

    <div class="surface-well divide-y divide-border-subtle px-4">
      {#each services as service (service.id)}
        <div class="grid gap-2 py-3">
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div class="flex min-w-0 items-start gap-3">
              <StatusLed
                status={rowTestResults[service.id]
                  ? rowTestResults[service.id].connected
                    ? "active"
                    : "error"
                  : "idle"}
                size="sm"
              />
              <div class="min-w-0 space-y-1">
                <div class="flex flex-wrap items-center gap-2">
                  <span class="truncate text-[0.82rem] font-medium text-text-primary">
                    {service.displayName}
                  </span>
                  <Badge variant="accent">{providerLabels[service.kind] ?? service.kind}</Badge>
                  {#if service.isDefault}<Badge>Default</Badge>{/if}
                  {#if !service.hasApiKey}<Badge variant="warning">No API key</Badge>{/if}
                </div>
                <p class="truncate font-mono text-[0.68rem] text-text-muted">{service.baseUrl}</p>
              </div>
            </div>
            <div class="flex items-center gap-1">
              <Button
                type="button"
                variant="ghost"
                size="icon"
                disabled={rowTestingId === service.id}
                onclick={() => void testServiceRow(service)}
                aria-label={`Test connection to ${service.displayName}`}
              >
                {#if rowTestingId === service.id}
                  <Loader2 class="h-4 w-4 animate-spin" />
                {:else}
                  <PlugZap class="h-4 w-4" />
                {/if}
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                onclick={() => openEditForm(service)}
                aria-label={`Edit ${service.displayName}`}
              >
                <Pencil class="h-4 w-4" />
              </Button>
              <Button
                type="button"
                variant="ghost"
                size="icon"
                onclick={() => void deleteService(service)}
                aria-label={`Delete ${service.displayName}`}
                class="text-error-text hover:bg-error-muted/20"
              >
                <Trash2 class="h-4 w-4" />
              </Button>
            </div>
          </div>
          {#if rowTestResults[service.id]}
            <p
              class="text-[0.72rem] {rowTestResults[service.id].connected
                ? 'text-success-text'
                : 'text-error-text'}"
            >
              {rowTestResults[service.id].message}
            </p>
          {/if}
        </div>
      {:else}
        <div class="flex flex-col items-center gap-1 py-8 text-center">
          <CloudDownload class="h-5 w-5 text-text-disabled" />
          <p class="text-[0.78rem] font-medium text-text-secondary">No request services yet</p>
          <p class="text-[0.68rem] text-text-muted">
            Add a Radarr, Sonarr, or Lidarr instance to enable the Request page.
          </p>
        </div>
      {/each}
    </div>
  </div>
</Panel>
