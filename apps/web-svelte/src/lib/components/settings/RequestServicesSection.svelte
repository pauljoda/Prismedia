<script lang="ts">
  import { onMount } from "svelte";
  import { CloudDownload, Loader2, Pencil, Plus, PlugZap, RefreshCw, Trash2 } from "@lucide/svelte";
  import { Badge, Button, Checkbox, Panel, Select, StatusLed, TextInput } from "@prismedia/ui-svelte";
  import { REQUEST_PROVIDER_KIND } from "$lib/api/generated/codes";
  import type { RequestProviderKindCode } from "$lib/api/generated/codes";
  import {
    deleteRequestServiceInstance,
    fetchRequestServiceOptions,
    fetchRequestServices,
    saveRequestServiceInstance,
    testRequestServiceInstance,
  } from "$lib/api/requests";
  import type {
    RequestConnectionTestResponse,
    RequestServiceInstanceSaveRequest,
    RequestServiceInstanceSummary,
    RequestServiceOptionsResponse,
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

  let services = $state<RequestServiceInstanceSummary[]>([]);
  let formOpen = $state(false);
  let editingServiceId = $state<string | null>(null);
  let saving = $state(false);
  let testingServiceId = $state<string | null>(null);
  let testResults = $state<Record<string, RequestConnectionTestResponse>>({});
  let serviceOptions = $state<RequestServiceOptionsResponse | null>(null);
  let loadingOptions = $state(false);
  let form = $state<RequestServiceInstanceSaveRequest>(emptyForm());

  const isLidarr = $derived(form.kind === REQUEST_PROVIDER_KIND.lidarr);

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
      searchOnRequest: true,
      isDefault: false,
    };
  }

  function openNewForm() {
    formOpen = true;
    editingServiceId = null;
    form = emptyForm();
    serviceOptions = null;
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
      searchOnRequest: service.searchOnRequest,
      isDefault: service.isDefault,
    };
    serviceOptions = null;
    void loadOptions(service.id, { quiet: true });
  }

  function closeForm() {
    formOpen = false;
    editingServiceId = null;
    form = emptyForm();
    serviceOptions = null;
  }

  async function saveService() {
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
      });
      services = await fetchRequestServices();
      onMessage(`${saved.displayName} saved.`);
      openEditForm(saved);
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

  async function testService(service: RequestServiceInstanceSummary) {
    testingServiceId = service.id;
    try {
      testResults = {
        ...testResults,
        [service.id]: await testRequestServiceInstance(service.id),
      };
    } catch (err) {
      onError(err instanceof Error ? err.message : "Failed to test request service");
    } finally {
      testingServiceId = null;
    }
  }

  async function loadOptions(serviceId = editingServiceId, { quiet = false } = {}) {
    if (!serviceId) return;
    loadingOptions = true;
    try {
      const options = await fetchRequestServiceOptions(serviceId);
      serviceOptions = options;
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
      form = {
        ...form,
        defaultRootFolderPath: rootFolder?.path ?? form.defaultRootFolderPath,
        defaultQualityProfileId:
          numericValue(qualityProfile?.id) ?? numericValue(form.defaultQualityProfileId),
        defaultMetadataProfileId:
          numericValue(metadataProfile?.id) ?? numericValue(form.defaultMetadataProfileId),
      };
      if (!quiet) onMessage("Loaded options from the service.");
    } catch (err) {
      if (!quiet) {
        onError(err instanceof Error ? err.message : "Failed to load service options");
      }
    } finally {
      loadingOptions = false;
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
        class="surface-well space-y-3 p-4"
        onsubmit={(event) => {
          event.preventDefault();
          void saveService();
        }}
      >
        <div class="grid gap-3 sm:grid-cols-2">
          <label class="space-y-1">
            <span class="text-label text-text-muted">Provider</span>
            <Select
              size="sm"
              value={form.kind}
              options={providerOptions}
              disabled={!!editingServiceId}
              onchange={(value) => (form = { ...form, kind: value as RequestProviderKindCode })}
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
              oninput={(event) => (form = { ...form, baseUrl: event.currentTarget.value })}
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
              oninput={(event) => (form = { ...form, apiKey: event.currentTarget.value })}
              placeholder={editingServiceId ? "Saved key kept unless replaced" : ""}
              autocomplete="new-password"
            />
          </label>
        </div>

        <div class="space-y-2">
          <div class="flex flex-wrap items-center justify-between gap-2">
            <span class="text-label text-text-muted">Request defaults</span>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              disabled={loadingOptions || !editingServiceId}
              onclick={() => void loadOptions()}
              class="gap-1.5 px-2.5 py-1 text-xs"
              title={editingServiceId
                ? "Fetch root folders and profiles from the service"
                : "Save the service first, then load its options"}
            >
              {#if loadingOptions}
                <Loader2 class="h-3.5 w-3.5 animate-spin" />
              {:else}
                <RefreshCw class="h-3.5 w-3.5" />
              {/if}
              Load from service
            </Button>
          </div>
          <div class="grid gap-3 sm:grid-cols-2 {isLidarr ? 'lg:grid-cols-3' : ''}">
            <label class="space-y-1">
              <span class="text-label text-text-muted">Root folder</span>
              {#if serviceOptions?.rootFolders.length}
                <Select
                  size="sm"
                  value={form.defaultRootFolderPath ?? ""}
                  options={serviceOptions.rootFolders.map((option) => ({
                    value: option.path ?? option.id,
                    label: option.name,
                  }))}
                  onchange={(value) => (form = { ...form, defaultRootFolderPath: value })}
                />
              {:else}
                <TextInput
                  size="sm"
                  value={form.defaultRootFolderPath ?? ""}
                  oninput={(event) =>
                    (form = { ...form, defaultRootFolderPath: event.currentTarget.value })}
                  placeholder="/media/movies"
                />
              {/if}
            </label>
            <label class="space-y-1">
              <span class="text-label text-text-muted">Quality profile</span>
              {#if serviceOptions?.qualityProfiles.length}
                <Select
                  size="sm"
                  value={form.defaultQualityProfileId === null
                    ? ""
                    : String(form.defaultQualityProfileId)}
                  options={serviceOptions.qualityProfiles.map((option) => ({
                    value: option.id,
                    label: option.name,
                  }))}
                  onchange={(value) => (form = { ...form, defaultQualityProfileId: numericValue(value) })}
                />
              {:else}
                <TextInput
                  size="sm"
                  type="number"
                  value={form.defaultQualityProfileId ?? ""}
                  oninput={(event) =>
                    (form = {
                      ...form,
                      defaultQualityProfileId: numericValue(event.currentTarget.value),
                    })}
                  placeholder="Profile ID"
                />
              {/if}
            </label>
            {#if isLidarr}
              <label class="space-y-1">
                <span class="text-label text-text-muted">Metadata profile</span>
                {#if serviceOptions?.metadataProfiles.length}
                  <Select
                    size="sm"
                    value={form.defaultMetadataProfileId === null
                      ? ""
                      : String(form.defaultMetadataProfileId)}
                    options={serviceOptions.metadataProfiles.map((option) => ({
                      value: option.id,
                      label: option.name,
                    }))}
                    onchange={(value) =>
                      (form = { ...form, defaultMetadataProfileId: numericValue(value) })}
                  />
                {:else}
                  <TextInput
                    size="sm"
                    type="number"
                    value={form.defaultMetadataProfileId ?? ""}
                    oninput={(event) =>
                      (form = {
                        ...form,
                        defaultMetadataProfileId: numericValue(event.currentTarget.value),
                      })}
                    placeholder="Profile ID"
                  />
                {/if}
              </label>
            {/if}
          </div>
        </div>

        <div class="flex flex-wrap items-center justify-between gap-3">
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
          <div class="flex items-center gap-2">
            <Button type="button" variant="ghost" size="sm" onclick={closeForm} class="px-3 py-1.5 text-xs">
              Cancel
            </Button>
            <Button
              type="submit"
              variant="primary"
              size="sm"
              disabled={saving || !form.displayName.trim() || !form.baseUrl.trim()}
              class="gap-1.5 px-3 py-1.5 text-xs"
            >
              {#if saving}
                <Loader2 class="h-3.5 w-3.5 animate-spin" />
              {/if}
              {editingServiceId ? "Save Changes" : "Add Service"}
            </Button>
          </div>
        </div>
      </form>
    {/if}

    <div class="surface-well divide-y divide-border-subtle px-4">
      {#each services as service (service.id)}
        <div class="grid gap-2 py-3">
          <div class="flex flex-wrap items-start justify-between gap-3">
            <div class="flex min-w-0 items-start gap-3">
              <StatusLed
                status={testResults[service.id]
                  ? testResults[service.id].connected
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
                disabled={testingServiceId === service.id}
                onclick={() => void testService(service)}
                aria-label={`Test connection to ${service.displayName}`}
              >
                {#if testingServiceId === service.id}
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
          {#if testResults[service.id]}
            <p
              class="text-[0.72rem] {testResults[service.id].connected
                ? 'text-success-text'
                : 'text-error-text'}"
            >
              {testResults[service.id].message ??
                (testResults[service.id].connected ? "Connected" : "Connection failed")}
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
