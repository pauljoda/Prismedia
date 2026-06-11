<script lang="ts">
  import { page } from "$app/state";
  import { Check, ChevronLeft, Loader2, RefreshCw, Send, Settings } from "@lucide/svelte";
  import { Badge, Button, Checkbox, Select, TextInput } from "@prismedia/ui-svelte";
  import { goto } from "$app/navigation";
  import { fetchRequestDetail, fetchRequestServices, submitRequest } from "$lib/api/requests";
  import type { RequestMediaKindCode, RequestProviderKindCode } from "$lib/api/generated/codes";
  import { REQUEST_MEDIA_KIND, REQUEST_PROVIDER_KIND } from "$lib/api/generated/codes";
  import RequestPosterCard from "$lib/components/requests/RequestPosterCard.svelte";
  import RequestCastStrip from "$lib/components/requests/RequestCastStrip.svelte";
  import {
    REQUEST_KIND_LABELS,
    REQUEST_PROVIDER_LABELS,
    REQUEST_RATING_SOURCE_LABELS,
    buildRequestSubmitPayload,
    defaultSelectedChildIds,
    inferRequestSourceForKind,
    numericValue,
    optionDefaultsForService,
    requestRatingDisplay,
    selectDefaultService,
    thumbnailAspectForKind,
    trackedLabel,
  } from "$lib/requests/request-helpers";
  import type { RequestDetailResponse, RequestServiceInstanceSummary } from "$lib/requests/request-model";

  const params = $derived(page.params as { kind: RequestMediaKindCode; id: string });
  const sourceQuery = $derived(page.url.searchParams.get("source") as RequestProviderKindCode | null);
  const initialServiceId = $derived(page.url.searchParams.get("serviceId"));
  /** Query string of the originating search page, chained through detail links so back returns to live results. */
  const backQuery = $derived(page.url.searchParams.get("back"));
  /** Artist context when this album was opened from a discography. */
  const fromId = $derived(page.url.searchParams.get("fromId"));
  const fromTitle = $derived(page.url.searchParams.get("fromTitle"));

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
  let discographyFilter = $state("");

  const selectedService = $derived(
    services.find((service) => service.id === selectedServiceId) ?? null,
  );

  const matchingServices = $derived(
    (() => {
      const activeSource = detail?.source ?? sourceQuery ?? inferRequestSourceForKind(params.kind);
      return activeSource ? services.filter((service) => service.kind === activeSource) : [];
    })(),
  );

  const serviceOptions = $derived(
    detail?.serviceOptions ?? { qualityProfiles: [], rootFolders: [], metadataProfiles: [], tags: [] },
  );

  const isSeries = $derived(detail?.kind === REQUEST_MEDIA_KIND.series);
  const isArtist = $derived(detail?.kind === REQUEST_MEDIA_KIND.artist);

  const backHref = $derived(
    fromId
      ? artistHref(fromId)
      : backQuery
        ? `/request?${backQuery}`
        : "/request",
  );
  const backLabel = $derived(fromId ? `Back to ${fromTitle ?? "artist"}` : "Back to search");

  const filteredChildren = $derived(
    (() => {
      const children = detail?.children ?? [];
      const term = discographyFilter.trim().toLowerCase();
      if (!term) return children;
      return children.filter((child) => child.title.toLowerCase().includes(term));
    })(),
  );

  // Reload whenever the route target changes — discography links navigate between
  // detail pages in place, so onMount alone would leave stale content on screen.
  let loadedKey = $state("");
  $effect(() => {
    const key = `${params.kind}:${params.id}:${sourceQuery ?? ""}:${initialServiceId ?? ""}`;
    if (key === loadedKey) return;
    loadedKey = key;
    void initialize();
  });

  async function initialize() {
    loading = true;
    detail = null;
    error = null;
    message = null;
    selectedChildIds = [];
    discographyFilter = "";
    try {
      services = await fetchRequestServices();
      const resolvedSource = sourceQuery ?? inferRequestSourceForKind(params.kind);
      if (!resolvedSource) {
        throw new Error("This request kind does not map to a configured provider.");
      }

      const fallbackService = selectDefaultService(services, resolvedSource);
      selectedServiceId = initialServiceId ?? fallbackService?.id ?? "";
      detail = await fetchRequestDetail({
        source: resolvedSource,
        kind: params.kind,
        externalId: params.id,
        serviceId: selectedServiceId || null,
      });
      selectedChildIds = defaultSelectedChildIds(detail);
      const service =
        services.find((item) => item.id === selectedServiceId) ??
        selectDefaultService(services, detail.source);
      if (service) {
        selectedServiceId = service.id;
        applyServiceDefaults(service, detail.serviceOptions);
      }
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load request detail";
    } finally {
      loading = false;
    }
  }

  async function changeService(serviceId: string) {
    selectedServiceId = serviceId;
    const service = services.find((item) => item.id === serviceId);
    if (!detail) return;
    error = null;
    try {
      detail = await fetchRequestDetail({
        source: detail.source,
        kind: detail.kind,
        externalId: detail.externalId,
        serviceId,
      });
      selectedChildIds = defaultSelectedChildIds(detail);
      if (service) applyServiceDefaults(service, detail.serviceOptions);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to refresh detail";
    }
  }

  function applyServiceDefaults(
    service: RequestServiceInstanceSummary,
    options = serviceOptions,
  ) {
    const defaults = optionDefaultsForService(service, options);
    qualityProfileId = defaults.qualityProfileId;
    rootFolderPath = defaults.rootFolderPath;
    metadataProfileId = defaults.metadataProfileId;
    searchNow = defaults.searchNow;
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
      const response = await submitRequest(
        buildRequestSubmitPayload(detail, selectedService, {
          qualityProfileId,
          rootFolderPath,
          metadataProfileId,
          monitored,
          searchNow,
          selectedChildIds,
        }),
      );
      message = response.message ?? (detail.tracked ? "Request updated." : "Request submitted.");
      await refreshDetailAfterSubmit();
    } catch (err) {
      error = err instanceof Error ? err.message : "Request failed";
    } finally {
      submitting = false;
    }
  }

  /** A successful submit changes upstream state (newly tracked / new monitoring); re-pull so the page reflects it. */
  async function refreshDetailAfterSubmit() {
    if (!detail) return;
    try {
      detail = await fetchRequestDetail({
        source: detail.source,
        kind: detail.kind,
        externalId: detail.externalId,
        serviceId: selectedServiceId || null,
      });
      selectedChildIds = defaultSelectedChildIds(detail);
    } catch {
      // The submit already succeeded; a failed refresh should not surface as an error.
    }
  }

  function ratingLabel(value: RequestDetailResponse["rating"]) {
    const rating = numericValue(value);
    return rating === null || rating <= 0 ? null : rating.toFixed(1);
  }

  function artistHref(artistId: string) {
    const linkParams = new URLSearchParams({ source: REQUEST_PROVIDER_KIND.lidarr });
    if (selectedServiceId) linkParams.set("serviceId", selectedServiceId);
    if (backQuery) linkParams.set("back", backQuery);
    return `/request/${REQUEST_MEDIA_KIND.artist}/${encodeURIComponent(artistId)}?${linkParams.toString()}`;
  }

  function childHref(child: RequestDetailResponse["children"][number]) {
    if (!detail) return "#";
    const linkParams = new URLSearchParams({ source: detail.source });
    if (selectedServiceId) linkParams.set("serviceId", selectedServiceId);
    if (backQuery) linkParams.set("back", backQuery);
    // Albums opened from a discography carry their artist so the back link returns here.
    linkParams.set("fromId", detail.externalId);
    linkParams.set("fromTitle", detail.title);
    return `/request/${child.kind}/${encodeURIComponent(child.id)}?${linkParams.toString()}`;
  }

  function trackDuration(seconds: number | string | null | undefined) {
    const total = numericValue(seconds);
    if (total === null || total <= 0) return null;
    return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
  }
</script>

<svelte:head><title>{detail?.title ?? "Request"} · Prismedia</title></svelte:head>

<div class="space-y-5">
  <a
    href={backHref}
    class="inline-flex items-center gap-1 text-[0.78rem] font-medium text-text-muted transition-colors hover:text-text-primary"
  >
    <ChevronLeft class="h-3.5 w-3.5" />
    {backLabel}
  </a>

  {#if loading}
    <div class="flex items-center gap-2.5 p-6 text-text-muted">
      <Loader2 class="h-4 w-4 animate-spin" />
      <span class="text-sm">Loading request detail…</span>
    </div>
  {:else if error && !detail}
    <div class="surface-panel border-l-2 border-error px-4 py-2.5 text-sm text-error-text">
      {error}
    </div>
  {:else if detail}
    <!-- ── Hero ── -->
    <section class="relative overflow-hidden rounded-sm border border-border-subtle bg-surface-1">
      {#if detail.backdropUrl}
        <!-- The artwork fills the entire banner; scrims keep the text legible. -->
        <img
          src={detail.backdropUrl}
          alt=""
          aria-hidden="true"
          class="absolute inset-0 h-full w-full object-cover"
        />
        <div
          class="absolute inset-0"
          style:background="linear-gradient(90deg, rgba(7,8,11,0.92) 0%, rgba(7,8,11,0.62) 40%, rgba(7,8,11,0.18) 75%, rgba(7,8,11,0.05) 100%)"
        ></div>
        <div
          class="absolute inset-0"
          style:background="linear-gradient(180deg, rgba(7,8,11,0.1) 0%, rgba(7,8,11,0.35) 60%, rgba(7,8,11,0.78) 100%)"
        ></div>
      {/if}
      <div class="relative grid gap-4 p-4 md:grid-cols-[160px_minmax(0,1fr)] md:items-end md:p-6">
        {#if detail.posterUrl}
          <div
            class="w-32 overflow-hidden rounded-xs border border-border-subtle bg-surface-2 shadow-[0_10px_30px_rgba(0,0,0,0.55)] md:w-40"
            style:aspect-ratio={thumbnailAspectForKind(detail.kind)}
          >
            <img src={detail.posterUrl} alt="" class="h-full w-full object-cover" />
          </div>
        {/if}
        <div class="min-w-0 space-y-2.5">
          <div class="flex flex-wrap items-center gap-1.5">
            <Badge variant="accent">{REQUEST_KIND_LABELS[detail.kind] ?? detail.kind}</Badge>
            <Badge>{REQUEST_PROVIDER_LABELS[detail.source] ?? detail.source}</Badge>
            {#if detail.tracked}
              <Badge variant="accent" class="gap-1">
                <Check class="h-3 w-3" aria-hidden="true" />
                {trackedLabel(detail.source)}{detail.monitored === false ? " · Unmonitored" : ""}
              </Badge>
            {/if}
            {#if detail.certification}<Badge>{detail.certification}</Badge>{/if}
            {#if detail.ratings.length > 0}
              {#each detail.ratings as rating (rating.source)}
                <Badge class="gap-1 font-mono" title={`${REQUEST_RATING_SOURCE_LABELS[rating.source] ?? rating.source} rating`}>
                  <span class="text-text-muted">{REQUEST_RATING_SOURCE_LABELS[rating.source] ?? rating.source}</span>
                  {requestRatingDisplay(rating)}
                </Badge>
              {/each}
            {:else if ratingLabel(detail.rating)}
              <Badge>★ {ratingLabel(detail.rating)}</Badge>
            {/if}
          </div>
          <h1 class="text-[1.6rem] leading-tight md:text-[2.1rem]">{detail.title}</h1>
          {#if detail.subtitle}
            <p class="text-[0.92rem] font-medium text-text-secondary">{detail.subtitle}</p>
          {/if}
          {#if detail.year || detail.runtimeMinutes || detail.trackCount}
            <p class="font-mono text-[0.72rem] text-text-muted">
              {[
                detail.year,
                detail.runtimeMinutes ? `${detail.runtimeMinutes} min` : null,
                detail.trackCount ? `${detail.trackCount} tracks` : null,
              ]
                .filter(Boolean)
                .join(" · ")}
            </p>
          {/if}
          {#if detail.overview}
            <p class="max-w-3xl text-[0.82rem] leading-relaxed text-text-secondary">
              {detail.overview}
            </p>
          {/if}
        </div>
      </div>
    </section>

    <div class="grid items-start gap-5 lg:grid-cols-[minmax(0,1fr)_340px]">
      <!-- ── Metadata ── -->
      <section class="surface-panel space-y-4 p-5">
        {#if detail.tags.length > 0 || detail.studios.length > 0}
          <div class="space-y-2">
            <h2 class="text-kicker text-text-primary">Details</h2>
            <div class="flex flex-wrap gap-1.5">
              {#each detail.tags as tag}<Badge>{tag}</Badge>{/each}
              {#each detail.studios as studio}<Badge variant="info">{studio}</Badge>{/each}
            </div>
          </div>
        {/if}
        {#if detail.cast.length > 0}
          <div class="space-y-2">
            <h3 class="text-label text-text-muted">Cast</h3>
            <RequestCastStrip cast={detail.cast} />
          </div>
        {/if}
        {#if detail.credits.length > 0}
          <div class="space-y-1.5">
            <h3 class="text-label text-text-muted">{detail.cast.length > 0 ? "Crew" : "Cast & Crew"}</h3>
            <p class="text-[0.78rem] leading-relaxed text-text-secondary">
              {detail.credits.join(", ")}
            </p>
          </div>
        {/if}

        {#if detail.children.length > 0}
          {#if isSeries}
            <div class="space-y-2">
              <h3 class="text-label text-text-muted">Seasons</h3>
              <div class="surface-well divide-y divide-border-subtle px-3">
                {#each detail.children as child (child.id)}
                  <label class="flex cursor-pointer items-center justify-between gap-2.5 py-2 text-[0.8rem] text-text-secondary">
                    <span class="flex items-center gap-2.5">
                      <Checkbox
                        checked={selectedChildIds.includes(child.id)}
                        disabled={!child.requestable}
                        onchange={(event) => toggleChild(child.id, event.currentTarget.checked)}
                      />
                      <span>{child.title}</span>
                      {#if detail.tracked && child.monitored}
                        <span class="font-mono text-[0.62rem] text-text-accent">monitored</span>
                      {/if}
                    </span>
                    {#if numericValue(child.itemCount)}
                      <span class="font-mono text-[0.68rem] text-text-muted">
                        {child.itemCount} episodes
                      </span>
                    {/if}
                  </label>
                {/each}
              </div>
            </div>
          {:else}
            <div class="space-y-2.5">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <h3 class="text-label text-text-muted">
                  Discography
                  <span class="ml-1 font-mono text-[0.68rem] text-text-muted">
                    {filteredChildren.length}{discographyFilter ? ` / ${detail.children.length}` : ""}
                  </span>
                </h3>
                {#if detail.children.length > 8}
                  <div class="w-full sm:w-56">
                    <TextInput
                      value={discographyFilter}
                      oninput={(event) => (discographyFilter = event.currentTarget.value)}
                      placeholder="Filter albums…"
                      aria-label="Filter discography"
                      autocomplete="off"
                    />
                  </div>
                {/if}
              </div>
              {#if filteredChildren.length > 0}
                <div class="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4 xl:grid-cols-5">
                  {#each filteredChildren as child (child.id)}
                    <RequestPosterCard
                      href={childHref(child)}
                      title={child.title}
                      imageUrl={child.posterUrl}
                      aspect="1 / 1"
                      chips={[
                        numericValue(child.year) ? String(numericValue(child.year)) : null,
                        child.overview,
                      ]}
                      placeholder="music"
                    />
                  {/each}
                </div>
              {:else}
                <p class="text-[0.78rem] text-text-muted">No albums match that filter.</p>
              {/if}
              {#if isArtist && detail.source === REQUEST_PROVIDER_KIND.lidarr}
                <p class="text-[0.72rem] text-text-muted">
                  Open an album to review its tracks and request just that album. Requesting the
                  artist here adds their whole catalog to Lidarr instead.
                </p>
              {/if}
            </div>
          {/if}
        {/if}

        {#if detail.tracks.length > 0}
          <div class="space-y-2">
            <h3 class="text-label text-text-muted">Tracks ({detail.tracks.length})</h3>
            <div class="surface-well divide-y divide-border-subtle px-3">
              {#each detail.tracks as track (track.number)}
                <div class="flex items-center gap-2.5 py-1.5 text-[0.8rem]">
                  <span class="w-6 shrink-0 text-right font-mono text-[0.68rem] text-text-disabled">
                    {track.number}
                  </span>
                  <span class="min-w-0 flex-1 truncate text-text-secondary">{track.title}</span>
                  {#if trackDuration(track.durationSeconds)}
                    <span class="shrink-0 font-mono text-[0.68rem] text-text-muted">
                      {trackDuration(track.durationSeconds)}
                    </span>
                  {/if}
                </div>
              {/each}
            </div>
          </div>
        {/if}
      </section>

      <!-- ── Request panel ── -->
      <aside class="surface-panel space-y-3.5 p-5">
        <h2 class="text-kicker text-text-primary">
          {detail.tracked ? "Update Request" : "Send Request"}
        </h2>

        {#if matchingServices.length === 0}
          <p class="text-[0.78rem] leading-relaxed text-text-muted">
            No matching service is configured for this media type.
          </p>
          <Button
            type="button"
            variant="secondary"
            size="sm"
            onclick={() => void goto("/settings")}
            class="no-lift gap-1.5 px-3 py-1.5 text-xs"
          >
            <Settings class="h-3.5 w-3.5" />
            Open Settings
          </Button>
        {:else}
          <label class="block space-y-1">
            <span class="text-label text-text-muted">Service</span>
            <Select
              size="sm"
              value={selectedServiceId}
              options={matchingServices.map((service) => ({
                value: service.id,
                label: service.displayName,
              }))}
              onchange={(value) => void changeService(value)}
            />
          </label>

          {#if detail.tracked}
            <p class="text-[0.75rem] leading-relaxed text-text-muted">
              Already in {selectedService?.displayName ?? REQUEST_PROVIDER_LABELS[detail.source]}.
              Updating changes {isSeries ? "season monitoring" : "monitoring"} and can start a
              search — quality and folder settings stay as configured in the service.
            </p>
          {:else}
            <label class="block space-y-1">
              <span class="text-label text-text-muted">Root folder</span>
              <Select
                size="sm"
                value={rootFolderPath ?? ""}
                options={serviceOptions.rootFolders.map((option) => ({
                  value: option.path ?? option.id,
                  label: option.name,
                }))}
                disabled={serviceOptions.rootFolders.length === 0}
                onchange={(value) => (rootFolderPath = value)}
              />
            </label>
            <label class="block space-y-1">
              <span class="text-label text-text-muted">Quality profile</span>
              <Select
                size="sm"
                value={qualityProfileId === null ? "" : String(qualityProfileId)}
                options={serviceOptions.qualityProfiles.map((option) => ({
                  value: option.id,
                  label: option.name,
                }))}
                disabled={serviceOptions.qualityProfiles.length === 0}
                onchange={(value) => (qualityProfileId = numericValue(value))}
              />
            </label>
            {#if detail.source === REQUEST_PROVIDER_KIND.lidarr}
              <label class="block space-y-1">
                <span class="text-label text-text-muted">Metadata profile</span>
                <Select
                  size="sm"
                  value={metadataProfileId === null ? "" : String(metadataProfileId)}
                  options={serviceOptions.metadataProfiles.map((option) => ({
                    value: option.id,
                    label: option.name,
                  }))}
                  disabled={serviceOptions.metadataProfiles.length === 0}
                  onchange={(value) => (metadataProfileId = numericValue(value))}
                />
              </label>
            {/if}
          {/if}

          <div class="space-y-2 pt-1">
            <label class="flex cursor-pointer items-center gap-2 text-[0.78rem] text-text-secondary">
              <Checkbox
                checked={monitored}
                onchange={(event) => (monitored = event.currentTarget.checked)}
              />
              {detail.tracked ? "Keep monitored" : "Monitor after adding"}
            </label>
            <label class="flex cursor-pointer items-center gap-2 text-[0.78rem] text-text-secondary">
              <Checkbox
                checked={searchNow}
                onchange={(event) => (searchNow = event.currentTarget.checked)}
              />
              Search after request
            </label>
          </div>

          {#if error}
            <p class="text-[0.75rem] leading-relaxed text-error-text">{error}</p>
          {/if}
          {#if message}
            <p class="flex items-center gap-1.5 text-[0.78rem] text-success-text">
              <Check class="h-3.5 w-3.5" />
              {message}
            </p>
            <a
              href="/request/history"
              class="inline-flex items-center gap-1 text-[0.72rem] font-medium text-text-muted transition-colors hover:text-text-primary"
            >
              View request history
            </a>
          {/if}

          <Button
            type="button"
            variant="primary"
            disabled={!selectedService || submitting}
            onclick={() => void handleSubmit()}
            class="w-full gap-2"
          >
            {#if submitting}
              <Loader2 class="h-4 w-4 animate-spin" />
            {:else if detail.tracked}
              <RefreshCw class="h-4 w-4" />
            {:else}
              <Send class="h-4 w-4" />
            {/if}
            {submitting ? "Submitting…" : detail.tracked ? "Update Request" : "Submit Request"}
          </Button>
        {/if}
      </aside>
    </div>
  {/if}
</div>
