<script lang="ts">
  import { onMount } from "svelte";
  import {
    AlertTriangle,
    Compass,
    HardDriveDownload,
    History,
    Loader2,
    PackageSearch,
    Send,
    Settings,
  } from "@lucide/svelte";
  import { Button, cn } from "@prismedia/ui-svelte";
  import type { SelectOption } from "@prismedia/ui-svelte";
  import { goto } from "$app/navigation";
  import { REQUEST_KIND_MANIFEST } from "$lib/api/generated/codes";
  import type { AcquisitionHistoryView } from "$lib/api/generated/model";
  import { fetchAcquisitionHistory } from "$lib/api/acquisitions";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import AcquisitionHistoryList from "$lib/components/acquisitions/AcquisitionHistoryList.svelte";
  import DownloadsPanel from "$lib/components/acquisitions/DownloadsPanel.svelte";
  import WantedList from "$lib/components/acquisitions/WantedList.svelte";
  import RequestDiscover from "$lib/components/requests/RequestDiscover.svelte";
  import { usePageSnapshots } from "$lib/stores/page-snapshots.svelte";

  const tabs = [
    { id: "discover", label: "Discover", icon: Compass },
    { id: "downloads", label: "Downloads", icon: HardDriveDownload },
    { id: "missing", label: "Missing", icon: PackageSearch },
    { id: "cutoff", label: "Cutoff unmet", icon: AlertTriangle },
    { id: "history", label: "History", icon: History },
  ] as const;
  type RequestTab = (typeof tabs)[number]["id"];
  let activeTab = $state<RequestTab>("discover");

  // Wanted filters are concrete acquisition units from the backend registry, not container kinds.
  const wantedKinds = [...new Set(
    REQUEST_KIND_MANIFEST
      .filter((descriptor) => descriptor.committable)
      .map((descriptor) => descriptor.acquisitionKind),
  )];
  const wantedKindOptions: SelectOption[] = [
    { value: "all", label: "All kinds" },
    ...wantedKinds.map(
      (kind) => ({ value: kind, label: labelForEntityKind(kind) }),
    ),
  ];

  // Durable acquisition activity log (global, newest-first), loaded lazily when the History tab opens.
  let history = $state<AcquisitionHistoryView[]>([]);
  let historyLoading = $state(false);
  let historyError = $state<string | null>(null);
  let historyLoaded = false;

  async function loadHistory() {
    if (historyLoaded) return;
    historyLoading = true;
    historyError = null;
    try {
      history = await fetchAcquisitionHistory({ limit: 200 });
      historyLoaded = true;
    } catch (err) {
      historyError = err instanceof Error ? err.message : "Failed to load history";
    } finally {
      historyLoading = false;
    }
  }

  $effect(() => {
    if (activeTab === "history") void loadHistory();
  });

  // Preserve the active tab across navigation so returning from proposal review lands back on Requests
  // rather than resetting to Discover.
  const pageSnapshots = usePageSnapshots();
  onMount(() =>
    pageSnapshots.registerSurface<{ tab: RequestTab }>("request-view", {
      capture: () => ({ tab: activeTab }),
      restore: (snapshot) => {
        // Ignore a snapshot naming a tab that no longer exists (e.g. the retired "requests" tab).
        if (tabs.some((tab) => tab.id === snapshot.tab)) {
          activeTab = snapshot.tab;
        }
      },
    }),
  );

</script>

<svelte:head><title>Request · Prismedia</title></svelte:head>

<div class="space-y-5">
  <!-- ── Header ── -->
  <div class="flex flex-wrap items-start justify-between gap-3">
    <div>
      <h1 class="flex items-center gap-2.5">
        <Send class="h-5 w-5 text-text-accent" />
        Request
      </h1>
      <p class="mt-1 text-[0.78rem] text-text-muted">
        Choose a content kind and metadata source, then review exactly what Prismedia will request
      </p>
    </div>
    <Button
      type="button"
      variant="secondary"
      size="sm"
      onclick={() => void goto("/settings/acquisition")}
      class="no-lift gap-1.5 px-3 py-1.5 text-xs"
    >
      <Settings class="h-3.5 w-3.5" />
      Settings
    </Button>
  </div>

  <!-- ── Tabs ── -->
  <div class="primary-tabs-rail">
    <div class="primary-tabs" role="tablist" aria-label="Request views">
      {#each tabs as tab (tab.id)}
        {@const TabIcon = tab.icon}
        <button
          type="button"
          role="tab"
          aria-selected={activeTab === tab.id}
          onclick={() => (activeTab = tab.id)}
          class={cn("primary-tab", activeTab === tab.id && "is-active")}
        >
          <TabIcon class="h-4 w-4" />
          {tab.label}
        </button>
      {/each}
    </div>
  </div>

  {#if activeTab === "downloads"}
    <!-- ── Global downloads: every active acquisition in one shared card list, live telemetry included ── -->
    <DownloadsPanel />
  {:else if activeTab === "missing"}
    <WantedList variant="missing" kindOptions={wantedKindOptions} />
  {:else if activeTab === "cutoff"}
    <WantedList variant="cutoffUnmet" kindOptions={wantedKindOptions} />
  {:else if activeTab === "history"}
    <!-- ── Activity log (global, newest-first) ── -->
    {#if historyError}
      <div class="surface-panel border-l-2 border-error px-4 py-2.5 text-sm text-error-text">
        {historyError}
      </div>
    {/if}
    {#if history.length > 0}
      <AcquisitionHistoryList entries={history} showKind />
    {:else if historyLoading}
      <div class="flex items-center justify-center gap-2.5 p-10 text-text-muted">
        <Loader2 class="h-4 w-4 animate-spin" />
        <span class="text-sm">Loading history…</span>
      </div>
    {:else}
      <div class="empty-rack-slot p-8 text-center">
        <p class="text-sm text-text-muted">
          No acquisition activity yet. Grabs, imports, failures, and removals will appear here.
        </p>
      </div>
    {/if}
  {:else}
    <RequestDiscover />
  {/if}
</div>

<style>
  /* Primary mode tabs (Discover / Requests): the app's underline-glow tab treatment, scaled up for
     top-level navigation. The tab row scrolls within itself on narrow screens (five tabs overflow a
     phone) so the page never scrolls horizontally; the scrollbar is hidden — the cut-off tab affords
     the swipe. The baseline underline lives on the non-scrolling rail so it always spans the row. */
  .primary-tabs-rail {
    position: relative;
  }

  .primary-tabs {
    display: flex;
    gap: 0.25rem;
    overflow-x: auto;
    scrollbar-width: none;
    -webkit-overflow-scrolling: touch;
  }

  .primary-tabs::-webkit-scrollbar {
    display: none;
  }

  .primary-tabs-rail::after {
    content: "";
    position: absolute;
    inset: auto 0 0 0;
    height: 1px;
    background: linear-gradient(
      to right,
      transparent,
      var(--color-border-subtle) 8%,
      var(--color-border-subtle) 92%,
      transparent
    );
    pointer-events: none;
  }

  .primary-tab {
    position: relative;
    display: inline-flex;
    flex: 0 0 auto;
    white-space: nowrap;
    align-items: center;
    gap: 0.5rem;
    background: transparent;
    color: var(--color-text-muted);
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.92rem;
    font-weight: 600;
    line-height: 1;
    padding: 0.65rem 0.9rem;
    transition: color var(--duration-fast, 120ms) var(--ease-default);
  }

  .primary-tab::before {
    content: "";
    position: absolute;
    inset: auto 0.35rem 0 0.35rem;
    height: 2px;
    background: transparent;
    transition:
      background var(--duration-normal, 200ms) var(--ease-mechanical),
      box-shadow var(--duration-normal, 200ms) var(--ease-mechanical);
    z-index: 1;
  }

  .primary-tab:hover {
    color: var(--color-text-secondary);
  }

  .primary-tab:hover::before {
    background: rgb(255 255 255 / 0.16);
  }

  .primary-tab:focus-visible {
    outline: 1px solid rgb(242 194 106 / 0.72);
    outline-offset: 2px;
    border-radius: var(--radius-xs, 4px);
  }

  .primary-tab.is-active {
    color: var(--color-text-accent-bright, #f2c26a);
  }

  .primary-tab.is-active::before {
    background: linear-gradient(
      to right,
      var(--color-accent-overlay-faint),
      var(--color-accent-overlay-strong) 50%,
      var(--color-accent-overlay-faint)
    );
    box-shadow:
      0 0 8px var(--color-accent-overlay-light),
      0 0 16px rgba(196, 154, 90, 0.1);
  }
</style>
