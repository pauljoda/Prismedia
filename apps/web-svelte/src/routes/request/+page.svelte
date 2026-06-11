<script lang="ts">
  import { onMount } from "svelte";
  import { PlugZap, Search, Settings, Trash2 } from "@lucide/svelte";
  import { Badge, Button, Checkbox, Select, TextInput } from "@prismedia/ui-svelte";
  import { REQUEST_MEDIA_KIND, REQUEST_PROVIDER_KIND } from "$lib/api/generated/codes";
  import {
    deleteRequestServiceInstance,
    fetchRequestServiceOptions,
    fetchRequestServices,
    saveRequestServiceInstance,
    searchRequests,
    testRequestServiceInstance,
  } from "$lib/api/requests";
  import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
  import type {
    RequestConnectionTestResponse,
    RequestSearchResult,
    RequestServiceInstanceSaveRequest,
    RequestServiceInstanceSummary,
    RequestServiceOptionsResponse,
  } from "$lib/requests/request-model";
  import { numericValue } from "$lib/requests/request-helpers";

  const kindOptions: { label: string; value: RequestMediaKindCode | "all" }[] = [
    { label: "All", value: "all" },
    { label: "Movies", value: REQUEST_MEDIA_KIND.movie },
    { label: "Series", value: REQUEST_MEDIA_KIND.series },
    { label: "Artists", value: REQUEST_MEDIA_KIND.artist },
    { label: "Albums", value: REQUEST_MEDIA_KIND.album },
    { label: "Plugins", value: REQUEST_MEDIA_KIND.plugin },
  ];

  let query = $state("");
  let selectedKind = $state<RequestMediaKindCode | "all">("all");
  let selectedSource = $state<RequestProviderKindCode | "all">("all");
  let services = $state<RequestServiceInstanceSummary[]>([]);
  let results = $state<RequestSearchResult[]>([]);
  let configOpen = $state(false);
  let editingServiceId = $state<string | null>(null);
  let savingService = $state(false);
  let serviceMessage = $state<string | null>(null);
  let serviceTestResults = $state<Record<string, RequestConnectionTestResponse>>({});
  let serviceOptions = $state<RequestServiceOptionsResponse | null>(null);
  let loadingServiceOptions = $state(false);
  let serviceForm = $state<RequestServiceInstanceSaveRequest>(emptyServiceForm());
  let loading = $state(false);
  let error = $state<string | null>(null);

  onMount(async () => {
    try {
      services = await fetchRequestServices();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load request services";
    }
  });

  async function runSearch() {
    if (!query.trim()) return;
    loading = true;
    error = null;
    try {
      const response = await searchRequests({
        query: query.trim(),
        kinds: selectedKind === "all" ? [] : [selectedKind],
        sources: selectedSource === "all" ? [] : [selectedSource],
      });
      results = response.results;
      if (response.providerErrors.length > 0) {
        error = response.providerErrors.map((item) => `${item.displayName}: ${item.message}`).join(" ");
      }
    } catch (err) {
      error = err instanceof Error ? err.message : "Search failed";
    } finally {
      loading = false;
    }
  }

  function sourceName(source: RequestProviderKindCode) {
    return services.find((service) => service.kind === source)?.displayName ?? source;
  }

  function detailHref(result: RequestSearchResult) {
    const params = new URLSearchParams({ source: result.source, serviceId: result.serviceId });
    return `/request/${result.kind}/${encodeURIComponent(result.externalId)}?${params.toString()}`;
  }

  function ratingLabel(value: RequestSearchResult["rating"]) {
    const rating = numericValue(value);
    return rating === null ? null : rating.toFixed(1);
  }

  function emptyServiceForm(): RequestServiceInstanceSaveRequest {
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

  function editService(service: RequestServiceInstanceSummary) {
    configOpen = true;
    editingServiceId = service.id;
    serviceForm = {
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
    serviceMessage = service.hasApiKey ? "API key is saved; enter a new key only to replace it." : null;
    serviceOptions = null;
  }

  function newService() {
    configOpen = true;
    editingServiceId = null;
    serviceForm = emptyServiceForm();
    serviceMessage = null;
    serviceOptions = null;
  }

  async function saveService() {
    savingService = true;
    error = null;
    serviceMessage = null;
    try {
      const saved = await saveRequestServiceInstance({
        ...serviceForm,
        displayName: serviceForm.displayName.trim(),
        baseUrl: serviceForm.baseUrl.trim(),
        apiKey: serviceForm.apiKey?.trim() || null,
        defaultRootFolderPath: serviceForm.defaultRootFolderPath?.trim() || null,
        defaultQualityProfileId: numericValue(serviceForm.defaultQualityProfileId),
        defaultMetadataProfileId: numericValue(serviceForm.defaultMetadataProfileId),
      });
      services = await fetchRequestServices();
      editService(saved);
      await loadServiceOptions(saved.id);
      serviceMessage = "Service saved. API key is redacted after save.";
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to save service";
    } finally {
      savingService = false;
    }
  }

  async function deleteService(service: RequestServiceInstanceSummary) {
    if (!window.confirm(`Delete ${service.displayName}?`)) return;
    error = null;
    try {
      await deleteRequestServiceInstance(service.id);
      services = await fetchRequestServices();
      if (editingServiceId === service.id) newService();
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to delete service";
    }
  }

  async function testService(service: RequestServiceInstanceSummary) {
    error = null;
    try {
      serviceTestResults = {
        ...serviceTestResults,
        [service.id]: await testRequestServiceInstance(service.id),
      };
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to test service";
    }
  }

  async function loadServiceOptions(serviceId = editingServiceId) {
    if (!serviceId) {
      serviceMessage = "Save the service before loading Arr options.";
      return;
    }

    loadingServiceOptions = true;
    error = null;
    try {
      const options = await fetchRequestServiceOptions(serviceId);
      serviceOptions = options;
      const rootFolder = options.rootFolders.find((option) => option.path === serviceForm.defaultRootFolderPath) ?? options.rootFolders[0];
      const qualityProfile = options.qualityProfiles.find((option) => numericValue(option.id) === numericValue(serviceForm.defaultQualityProfileId)) ?? options.qualityProfiles[0];
      const metadataProfile = options.metadataProfiles.find((option) => numericValue(option.id) === numericValue(serviceForm.defaultMetadataProfileId)) ?? options.metadataProfiles[0];
      serviceForm = {
        ...serviceForm,
        defaultRootFolderPath: rootFolder?.path ?? rootFolder?.id ?? serviceForm.defaultRootFolderPath,
        defaultQualityProfileId: numericValue(qualityProfile?.id) ?? numericValue(serviceForm.defaultQualityProfileId),
        defaultMetadataProfileId: numericValue(metadataProfile?.id) ?? numericValue(serviceForm.defaultMetadataProfileId),
      };
      serviceMessage = "Loaded Arr options.";
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load service options";
    } finally {
      loadingServiceOptions = false;
    }
  }
</script>

<svelte:head><title>Request · Prismedia</title></svelte:head>

<main class="request-page">
  <section class="request-header">
    <div>
      <p class="eyebrow">Operate</p>
      <h1>Request</h1>
    </div>
    <Button variant="secondary" onclick={() => (configOpen = !configOpen)}>
      <Settings size={16} />
      Services
    </Button>
    <form class="search-bar" onsubmit={(event) => { event.preventDefault(); void runSearch(); }}>
      <TextInput
        value={query}
        oninput={(event) => (query = event.currentTarget.value)}
        placeholder="Search movies, series, artists, albums"
        aria-label="Search requests"
      />
      <Button type="submit" disabled={loading || !query.trim()}>
        <Search size={16} />
        {loading ? "Searching" : "Search"}
      </Button>
    </form>
  </section>

  {#if configOpen}
    <section class="service-config" aria-label="Request service configuration">
      <div class="service-list">
        <div class="service-list-header">
          <h2>Services</h2>
          <Button size="sm" variant="secondary" onclick={newService}>Add</Button>
        </div>
        {#if services.length === 0}
          <p class="muted">No request services configured.</p>
        {:else}
          {#each services as service}
            <div class="service-row">
              <button type="button" class:active={editingServiceId === service.id} onclick={() => editService(service)}>
                <span>{service.displayName}</span>
                <small>{service.kind}{service.isDefault ? " · default" : ""}{service.hasApiKey ? " · key saved" : ""}</small>
              </button>
              <Button size="icon" variant="ghost" aria-label={`Test ${service.displayName}`} onclick={() => testService(service)}>
                <PlugZap size={15} />
              </Button>
              <Button size="icon" variant="danger" aria-label={`Delete ${service.displayName}`} onclick={() => deleteService(service)}>
                <Trash2 size={15} />
              </Button>
              {#if serviceTestResults[service.id]}
                <p class:ok={serviceTestResults[service.id].connected} class="test-result">
                  {serviceTestResults[service.id].message ?? (serviceTestResults[service.id].connected ? "Connected" : "Connection failed")}
                </p>
              {/if}
            </div>
          {/each}
        {/if}
      </div>

      <form class="service-form" onsubmit={(event) => { event.preventDefault(); void saveService(); }}>
        <h2>{editingServiceId ? "Edit service" : "Add service"}</h2>
        <label>
          <span>Provider</span>
          <Select
            value={serviceForm.kind}
            options={[
              { value: REQUEST_PROVIDER_KIND.radarr, label: "Radarr" },
              { value: REQUEST_PROVIDER_KIND.sonarr, label: "Sonarr" },
              { value: REQUEST_PROVIDER_KIND.lidarr, label: "Lidarr" },
            ]}
            onchange={(value) => (serviceForm = { ...serviceForm, kind: value as RequestProviderKindCode })}
          />
        </label>
        <label>
          <span>Name</span>
          <TextInput value={serviceForm.displayName} oninput={(event) => (serviceForm = { ...serviceForm, displayName: event.currentTarget.value })} required />
        </label>
        <label>
          <span>Base URL</span>
          <TextInput value={serviceForm.baseUrl} oninput={(event) => (serviceForm = { ...serviceForm, baseUrl: event.currentTarget.value })} placeholder="http://radarr:7878" required />
        </label>
        <label>
          <span>API key</span>
          <TextInput value={serviceForm.apiKey ?? ""} oninput={(event) => (serviceForm = { ...serviceForm, apiKey: event.currentTarget.value })} placeholder={editingServiceId ? "Saved key is redacted" : ""} autocomplete="off" />
        </label>
        <div class="form-grid">
          <label>
            <span>Default root folder</span>
            {#if serviceOptions?.rootFolders.length}
              <Select
                value={serviceForm.defaultRootFolderPath ?? ""}
                options={serviceOptions.rootFolders.map((option) => ({ value: option.path ?? option.id, label: option.name }))}
                onchange={(value) => (serviceForm = { ...serviceForm, defaultRootFolderPath: value })}
              />
            {:else}
              <TextInput value={serviceForm.defaultRootFolderPath ?? ""} oninput={(event) => (serviceForm = { ...serviceForm, defaultRootFolderPath: event.currentTarget.value })} placeholder="/media" />
            {/if}
          </label>
          <label>
            <span>Default quality profile ID</span>
            {#if serviceOptions?.qualityProfiles.length}
              <Select
                value={serviceForm.defaultQualityProfileId === null ? "" : String(serviceForm.defaultQualityProfileId)}
                options={serviceOptions.qualityProfiles.map((option) => ({ value: option.id, label: option.name }))}
                onchange={(value) => (serviceForm = { ...serviceForm, defaultQualityProfileId: numericValue(value) })}
              />
            {:else}
              <TextInput type="number" value={serviceForm.defaultQualityProfileId ?? ""} oninput={(event) => (serviceForm = { ...serviceForm, defaultQualityProfileId: numericValue(event.currentTarget.value) })} />
            {/if}
          </label>
          {#if serviceForm.kind === REQUEST_PROVIDER_KIND.lidarr}
            <label>
              <span>Default metadata profile ID</span>
              {#if serviceOptions?.metadataProfiles.length}
                <Select
                  value={serviceForm.defaultMetadataProfileId === null ? "" : String(serviceForm.defaultMetadataProfileId)}
                  options={serviceOptions.metadataProfiles.map((option) => ({ value: option.id, label: option.name }))}
                  onchange={(value) => (serviceForm = { ...serviceForm, defaultMetadataProfileId: numericValue(value) })}
                />
              {:else}
                <TextInput type="number" value={serviceForm.defaultMetadataProfileId ?? ""} oninput={(event) => (serviceForm = { ...serviceForm, defaultMetadataProfileId: numericValue(event.currentTarget.value) })} />
              {/if}
            </label>
          {/if}
        </div>
        <Button type="button" variant="secondary" disabled={loadingServiceOptions || !editingServiceId} onclick={() => loadServiceOptions()}>
          {loadingServiceOptions ? "Loading options" : "Load Arr options"}
        </Button>
        <label class="toggle-row">
          <Checkbox checked={serviceForm.searchOnRequest} onchange={(event) => (serviceForm = { ...serviceForm, searchOnRequest: event.currentTarget.checked })} />
          <span>Search after request by default</span>
        </label>
        <label class="toggle-row">
          <Checkbox checked={serviceForm.isDefault} onchange={(event) => (serviceForm = { ...serviceForm, isDefault: event.currentTarget.checked })} />
          <span>Default for this provider</span>
        </label>
        {#if serviceMessage}<p class="muted">{serviceMessage}</p>{/if}
        <Button type="submit" disabled={savingService || !serviceForm.displayName.trim() || !serviceForm.baseUrl.trim()}>
          {savingService ? "Saving" : "Save service"}
        </Button>
      </form>
    </section>
  {/if}

  <section class="filters" aria-label="Request filters">
    <div class="chip-row">
      {#each kindOptions as option}
        <Button
          variant={selectedKind === option.value ? "primary" : "secondary"}
          size="sm"
          onclick={() => (selectedKind = option.value)}
        >
          {option.label}
        </Button>
      {/each}
    </div>
    <div class="chip-row">
      <Button
        variant={selectedSource === "all" ? "primary" : "secondary"}
        size="sm"
        onclick={() => (selectedSource = "all")}
      >
        All sources
      </Button>
      {#each [REQUEST_PROVIDER_KIND.radarr, REQUEST_PROVIDER_KIND.sonarr, REQUEST_PROVIDER_KIND.lidarr, REQUEST_PROVIDER_KIND.plugin] as source}
        <Button
          variant={selectedSource === source ? "primary" : "secondary"}
          size="sm"
          onclick={() => (selectedSource = source)}
        >
          {source}
        </Button>
      {/each}
    </div>
  </section>

  {#if error}
    <p class="notice">{error}</p>
  {/if}

  <section class="results" aria-label="Request search results">
    {#if results.length === 0}
      <div class="empty">No request results loaded.</div>
    {:else}
      {#each results as result}
        <a class="result-row" href={detailHref(result)}>
          <div class="poster">
            {#if result.posterUrl}
              <img src={result.posterUrl} alt="" loading="lazy" />
            {/if}
          </div>
          <div class="result-copy">
            <div class="result-title">
              <h2>{result.title}</h2>
              {#if result.year}<span>{result.year}</span>{/if}
            </div>
            <p>{result.overview ?? "No overview available."}</p>
            <div class="badges">
              <Badge>{sourceName(result.source)}</Badge>
              <Badge variant="accent">{result.kind}</Badge>
              {#if ratingLabel(result.rating)}
                <Badge>{ratingLabel(result.rating)}</Badge>
              {/if}
              <Badge variant={result.requestable ? "success" : "warning"}>{result.requestable ? "Requestable" : "Unavailable"}</Badge>
            </div>
          </div>
        </a>
      {/each}
    {/if}
  </section>
</main>

<style>
  .request-page {
    display: grid;
    gap: 1rem;
    padding: 1rem;
  }

  .request-header {
    display: grid;
    gap: 1rem;
  }

  .eyebrow {
    color: var(--color-text-muted);
    font-size: 0.75rem;
    margin: 0 0 0.25rem;
    text-transform: uppercase;
  }

  h1, h2, p {
    margin: 0;
  }

  h1 {
    font-family: var(--font-display);
    font-size: 2rem;
  }

  .search-bar {
    display: grid;
    gap: 0.5rem;
  }

  .filters {
    display: grid;
    gap: 0.5rem;
  }

  .chip-row, .badges {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
  }

  .notice, .empty {
    border: 1px solid var(--color-border-subtle);
    border-radius: 6px;
    color: var(--color-text-secondary);
    padding: 0.75rem;
  }

  .results {
    display: grid;
    gap: 0.75rem;
  }

  .result-row {
    display: grid;
    grid-template-columns: 64px 1fr;
    gap: 0.875rem;
    padding: 0.75rem;
    border: 1px solid var(--color-border-subtle);
    border-radius: 8px;
    background: var(--color-surface-1);
    color: inherit;
    text-decoration: none;
  }

  .poster {
    aspect-ratio: 2 / 3;
    overflow: hidden;
    border-radius: 4px;
    background: var(--color-surface-2);
  }

  .poster img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }

  .result-copy {
    min-width: 0;
    display: grid;
    gap: 0.5rem;
  }

  .result-title {
    display: flex;
    gap: 0.5rem;
    align-items: baseline;
  }

  .result-title h2 {
    font-size: 1rem;
  }

  .result-copy p {
    color: var(--color-text-secondary);
    font-size: 0.875rem;
    line-height: 1.45;
  }

  @media (min-width: 760px) {
    .request-page {
      padding: 1.5rem;
    }

    .request-header {
    grid-template-columns: minmax(180px, 0.7fr) auto minmax(360px, 1.3fr);
      align-items: end;
    }

    .search-bar {
      grid-template-columns: 1fr auto;
    }

    .result-row {
      grid-template-columns: 92px 1fr;
    }
  }

  h2 {
    margin: 0;
    font-size: 1rem;
  }

  .service-config {
    display: grid;
    gap: 1rem;
    border: 1px solid var(--color-border-subtle);
    border-radius: 8px;
    background: var(--color-surface-1);
    padding: 1rem;
  }

  .service-list, .service-form {
    display: grid;
    gap: 0.75rem;
    align-content: start;
  }

  .service-list-header {
    display: flex;
    justify-content: space-between;
    gap: 0.75rem;
    align-items: center;
  }

  .service-row {
    display: grid;
    grid-template-columns: 1fr auto auto;
    gap: 0.4rem;
    align-items: center;
  }

  .service-row > button:first-child {
    display: grid;
    gap: 0.2rem;
    text-align: left;
    border: 1px solid var(--color-border-subtle);
    border-radius: 6px;
    padding: 0.6rem;
    background: var(--color-surface-2);
    color: var(--color-text-primary);
  }

  .service-row > button:first-child.active {
    border-color: var(--color-border-accent);
    box-shadow: var(--shadow-glow-accent);
  }

  .service-row small, .muted {
    color: var(--color-text-secondary);
  }

  .test-result {
    grid-column: 1 / -1;
    color: var(--color-error-text);
    margin: 0;
    font-size: 0.85rem;
  }

  .test-result.ok {
    color: var(--color-success-text);
  }

  .service-form label {
    display: grid;
    gap: 0.35rem;
    color: var(--color-text-secondary);
    font-size: 0.85rem;
  }

  .form-grid {
    display: grid;
    gap: 0.75rem;
  }

  .toggle-row {
    display: flex !important;
    flex-direction: row;
    align-items: center;
  }

  @media (min-width: 860px) {
    .service-config {
      grid-template-columns: minmax(260px, 0.8fr) minmax(360px, 1.2fr);
    }

    .form-grid {
      grid-template-columns: repeat(2, minmax(0, 1fr));
    }
  }
</style>
