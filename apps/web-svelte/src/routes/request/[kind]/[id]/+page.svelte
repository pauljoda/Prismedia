<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/stores";
  import { Check, Loader2 } from "@lucide/svelte";
  import { Badge, Button, Checkbox, Select } from "@prismedia/ui-svelte";
  import { fetchRequestDetail, fetchRequestServices, submitRequest } from "$lib/api/requests";
  import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
  import {
    buildRequestSubmitPayload,
    defaultSelectedChildIds,
    numericValue,
    selectDefaultService,
  } from "$lib/requests/request-helpers";
  import type { RequestDetailResponse, RequestServiceInstanceSummary } from "$lib/requests/request-model";

  const params = $derived($page.params as { kind: RequestMediaKindCode; id: string });
  const source = $derived(($page.url.searchParams.get("source") ?? "") as RequestProviderKindCode);
  const initialServiceId = $derived($page.url.searchParams.get("serviceId"));

  let services = $state<RequestServiceInstanceSummary[]>([]);
  let selectedServiceId = $state<string>("");
  let detail = $state<RequestDetailResponse | null>(null);
  let selectedChildIds = $state<string[]>([]);
  let qualityProfileId = $state<number | null>(null);
  let rootFolderPath = $state<string | null>(null);
  let metadataProfileId = $state<number | null>(null);
  let monitored = $state(true);
  let searchNow = $state(true);
  let loading = $state(true);
  let submitting = $state(false);
  let message = $state<string | null>(null);
  let error = $state<string | null>(null);

  const selectedService = $derived(
    services.find((service) => service.id === selectedServiceId) ?? null,
  );

  const matchingServices = $derived(
    (() => {
      const current = detail;
      return current ? services.filter((service) => service.kind === current.source) : [];
    })(),
  );

  onMount(async () => {
    try {
      services = await fetchRequestServices();
      const fallbackService = source ? selectDefaultService(services, source) : null;
      selectedServiceId = initialServiceId ?? fallbackService?.id ?? "";
      detail = await fetchRequestDetail({
        source,
        kind: params.kind,
        externalId: params.id,
        serviceId: selectedServiceId,
      });
      selectedChildIds = defaultSelectedChildIds(detail);
      const service = services.find((item) => item.id === selectedServiceId) ?? selectDefaultService(services, detail.source);
      if (service) applyServiceDefaults(service);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load request detail";
    } finally {
      loading = false;
    }
  });

  async function changeService(serviceId: string) {
    selectedServiceId = serviceId;
    const service = services.find((item) => item.id === serviceId);
    if (service) applyServiceDefaults(service);
    if (!detail) return;
    try {
      detail = await fetchRequestDetail({
        source: detail.source,
        kind: detail.kind,
        externalId: detail.externalId,
        serviceId,
      });
      selectedChildIds = defaultSelectedChildIds(detail);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to refresh detail";
    }
  }

  function applyServiceDefaults(service: RequestServiceInstanceSummary) {
    qualityProfileId = numericValue(service.defaultQualityProfileId);
    rootFolderPath = service.defaultRootFolderPath;
    metadataProfileId = numericValue(service.defaultMetadataProfileId);
  }

  function toggleChild(id: string, checked: boolean) {
    selectedChildIds = checked
      ? Array.from(new Set([...selectedChildIds, id]))
      : selectedChildIds.filter((childId) => childId !== id);
  }

  async function handleSubmit() {
    if (!detail || !selectedService) return;
    submitting = true;
    error = null;
    message = null;
    try {
      const response = await submitRequest(buildRequestSubmitPayload(detail, selectedService, {
        qualityProfileId,
        rootFolderPath,
        metadataProfileId,
        monitored,
        searchNow,
        selectedChildIds,
      }));
      message = response.message ?? "Request submitted.";
    } catch (err) {
      error = err instanceof Error ? err.message : "Request failed";
    } finally {
      submitting = false;
    }
  }

  function setBoolean(field: "monitored" | "searchNow", value: Event) {
    const checked = (value.currentTarget as HTMLInputElement).checked;
    if (field === "monitored") monitored = checked;
    else searchNow = checked;
  }

  function ratingLabel(value: RequestDetailResponse["rating"]) {
    const rating = numericValue(value);
    return rating === null ? null : rating.toFixed(1);
  }
</script>

<svelte:head><title>{detail?.title ?? "Request"} · Prismedia</title></svelte:head>

<main class="detail-page">
  {#if loading}
    <div class="state"><Loader2 size={18} /> Loading request detail</div>
  {:else if error && !detail}
    <div class="state error">{error}</div>
  {:else if detail}
    <section class="hero" style={`background-image: linear-gradient(90deg, rgba(10,10,12,.96), rgba(10,10,12,.76)), url('${detail.backdropUrl ?? detail.posterUrl ?? ""}')`}>
      <div class="poster">
        {#if detail.posterUrl}<img src={detail.posterUrl} alt="" />{/if}
      </div>
      <div class="hero-copy">
        <div class="badges">
          <Badge variant="accent">{detail.kind}</Badge>
          <Badge>{detail.source}</Badge>
          {#if ratingLabel(detail.rating)}
            <Badge>{ratingLabel(detail.rating)}</Badge>
          {/if}
          {#if detail.certification}<Badge>{detail.certification}</Badge>{/if}
        </div>
        <h1>{detail.title}</h1>
        <p>{[detail.year, detail.runtimeMinutes ? `${detail.runtimeMinutes} min` : null].filter(Boolean).join(" · ")}</p>
        <p class="overview">{detail.overview ?? "No overview available."}</p>
      </div>
    </section>

    <section class="request-grid">
      <div class="metadata">
        <h2>Metadata</h2>
        <div class="badges">
          {#each detail.tags as tag}<Badge>{tag}</Badge>{/each}
          {#each detail.studios as studio}<Badge>{studio}</Badge>{/each}
        </div>
        {#if detail.credits.length > 0}
          <h3>Cast & Crew</h3>
          <p>{detail.credits.join(", ")}</p>
        {/if}
        {#if detail.children.length > 0}
          <h3>{detail.kind === "series" ? "Seasons" : "Albums"}</h3>
          <div class="children">
            {#each detail.children as child}
              <label class="child-row">
                <Checkbox
                  checked={selectedChildIds.includes(child.id)}
                  onchange={(event) => toggleChild(child.id, event.currentTarget.checked)}
                />
                <span>{child.title}</span>
              </label>
            {/each}
          </div>
        {/if}
      </div>

      <aside class="request-panel">
        <h2>Request</h2>
        <label>
          <span>Service</span>
          <Select
            value={selectedServiceId}
            options={matchingServices.map((service) => ({ value: service.id, label: service.displayName }))}
            onchange={changeService}
          />
        </label>
        <label>
          <span>Root folder</span>
          <input bind:value={rootFolderPath} placeholder="/media" />
        </label>
        <label>
          <span>Quality profile</span>
          <input type="number" bind:value={qualityProfileId} />
        </label>
        {#if detail.source === "lidarr"}
          <label>
            <span>Metadata profile</span>
            <input type="number" bind:value={metadataProfileId} />
          </label>
        {/if}
        <label class="toggle-row">
          <Checkbox checked={monitored} onchange={(event) => setBoolean("monitored", event)} />
          <span>Monitor after adding</span>
        </label>
        <label class="toggle-row">
          <Checkbox checked={searchNow} onchange={(event) => setBoolean("searchNow", event)} />
          <span>Search after request</span>
        </label>
        {#if error}<p class="panel-error">{error}</p>{/if}
        {#if message}<p class="panel-message"><Check size={14} /> {message}</p>{/if}
        <Button onclick={handleSubmit} disabled={!selectedService || submitting}>
          {submitting ? "Submitting" : "Submit request"}
        </Button>
      </aside>
    </section>
  {/if}
</main>

<style>
  .detail-page {
    display: grid;
    gap: 1rem;
    padding: 1rem;
  }

  .state {
    color: var(--color-text-secondary);
    display: flex;
    gap: 0.5rem;
    align-items: center;
    padding: 1rem;
  }

  .error, .panel-error {
    color: var(--color-error-text);
  }

  .hero {
    min-height: 340px;
    border-radius: 8px;
    border: 1px solid var(--color-border-subtle);
    background-size: cover;
    background-position: center;
    display: grid;
    gap: 1rem;
    padding: 1rem;
  }

  .poster {
    width: 128px;
    aspect-ratio: 2 / 3;
    border-radius: 6px;
    overflow: hidden;
    background: var(--color-surface-2);
  }

  .poster img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }

  .hero-copy, .metadata, .request-panel {
    display: grid;
    gap: 0.75rem;
    align-content: start;
  }

  h1, h2, h3, p {
    margin: 0;
  }

  h1 {
    font-family: var(--font-display);
    font-size: clamp(2rem, 6vw, 4rem);
  }

  h2 {
    font-size: 1.1rem;
  }

  h3 {
    font-size: 0.9rem;
    color: var(--color-text-secondary);
  }

  .overview, .metadata p {
    color: var(--color-text-secondary);
    line-height: 1.55;
  }

  .badges {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
  }

  .request-grid {
    display: grid;
    gap: 1rem;
  }

  .request-panel, .metadata {
    border: 1px solid var(--color-border-subtle);
    border-radius: 8px;
    padding: 1rem;
    background: var(--color-surface-1);
  }

  .request-panel label {
    display: grid;
    gap: 0.35rem;
    color: var(--color-text-secondary);
    font-size: 0.85rem;
  }

  .request-panel input {
    min-height: 2.25rem;
    border-radius: 4px;
    border: 1px solid var(--color-border-default);
    background: var(--color-surface-2);
    color: var(--color-text-primary);
    padding: 0 0.65rem;
  }

  .toggle-row, .child-row {
    display: flex !important;
    flex-direction: row;
    gap: 0.5rem !important;
    align-items: center;
  }

  .children {
    display: grid;
    gap: 0.4rem;
  }

  .panel-message {
    display: flex;
    align-items: center;
    gap: 0.35rem;
    color: var(--color-success-text);
  }

  @media (min-width: 860px) {
    .detail-page {
      padding: 1.5rem;
    }

    .hero {
      grid-template-columns: 180px 1fr;
      align-items: end;
      padding: 1.5rem;
    }

    .poster {
      width: 180px;
    }

    .request-grid {
      grid-template-columns: minmax(0, 1fr) 340px;
      align-items: start;
    }
  }
</style>
