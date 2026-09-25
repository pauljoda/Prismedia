<script lang="ts">
  import { onMount } from "svelte";
  import { ArrowRight, Puzzle, ShieldUser, SquareArrowOutUpRight } from "@lucide/svelte";
  import { Alert, StatusLed, buttonVariants } from "@prismedia/ui-svelte";
  import type { ConnectionResponse, PluginProvider } from "$lib/api/generated/model";
  import { fetchConnections } from "$lib/api/connections";
  import { fetchPluginProviders } from "$lib/api/plugins";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import PrismPageHeader from "$lib/components/concepts/PrismPageHeader.svelte";
  import LightCoverage from "$lib/components/concepts/manage/LightCoverage.svelte";
  import ManageConceptFrame from "$lib/components/concepts/manage/ManageConceptFrame.svelte";
  import PluginAvailableRow from "$lib/components/concepts/manage/PluginAvailableRow.svelte";
  import PluginLightCard from "$lib/components/concepts/manage/PluginLightCard.svelte";
  import { familyCoverage } from "$lib/components/concepts/manage/plugin-light";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useSession } from "$lib/stores/session.svelte";

  const session = useSession();
  const nsfw = useNsfw();

  let plugins = $state.raw<PluginProvider[] | null>(null);
  let connections = $state.raw<ConnectionResponse[]>([]);
  let error = $state<string | null>(null);

  onMount(() => {
    if (!session.isAdmin) return;
    fetchPluginProviders().then(
      (list) => (plugins = list),
      (cause: unknown) => {
        plugins = [];
        error = cause instanceof Error ? cause.message : "Could not load plugins";
      },
    );
    // Connections enrich the cards; the page still works without them.
    fetchConnections().then(
      (list) => (connections = list),
      () => undefined,
    );
  });

  const visible = $derived((plugins ?? []).filter((plugin) => nsfw.mode !== "off" || !plugin.isNsfw));
  const installed = $derived(
    visible
      .filter((plugin) => plugin.installed)
      .sort((left, right) => Number(right.enabled) - Number(left.enabled) || left.name.localeCompare(right.name)),
  );
  const available = $derived(
    visible.filter((plugin) => !plugin.installed).sort((left, right) => left.name.localeCompare(right.name)),
  );
  const lit = $derived(installed.filter((plugin) => plugin.enabled));
  const coverage = $derived(familyCoverage(lit));
  const needsCredentials = $derived(lit.filter((plugin) => plugin.missingAuthKeys.length > 0).length);
  const updates = $derived(lit.filter((plugin) => plugin.updateAvailable).length);
  const connectionsByPlugin = $derived.by(() => {
    const grouped = new Map<string, ConnectionResponse[]>();
    for (const connection of connections) {
      const list = grouped.get(connection.pluginId);
      if (list) list.push(connection);
      else grouped.set(connection.pluginId, [connection]);
    }
    return grouped;
  });
</script>

<svelte:head><title>Plugin cards · Concepts · Prismedia</title></svelte:head>

<ManageConceptFrame concept="Plugin cards">
  {#if !session.isAdmin}
    <StatePlaceholder icon={ShieldUser} title="Administrator access required" />
  {:else}
    <PrismPageHeader icon={Puzzle} title="Plugins">
      {#snippet status()}
        {#if plugins}
          <span class="text-caption text-text-muted">
            <span class="font-mono text-text-secondary">{installed.length}</span> installed ·
            <span class="font-mono text-text-secondary">{available.length}</span> available
          </span>
          {#if needsCredentials > 0}
            <span class="flex items-center gap-2 text-caption text-text-secondary">
              <StatusLed status="warning" size="sm" />
              <span class="font-mono text-text-primary">{needsCredentials}</span> need keys
            </span>
          {/if}
          {#if updates > 0}
            <span class="flex items-center gap-2 text-caption text-text-secondary">
              <StatusLed status="info" size="sm" />
              <span class="font-mono text-text-primary">{updates}</span> {updates === 1 ? "update" : "updates"}
            </span>
          {/if}
        {/if}
      {/snippet}
      {#snippet actions()}
        <a href="/plugins" class={buttonVariants({ variant: "outline", size: "sm" })}>
          <SquareArrowOutUpRight aria-hidden="true" />
          Plugin manager
        </a>
      {/snippet}
    </PrismPageHeader>

    {#if error}
      <Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>
    {/if}

    {#if plugins === null}
      <StatePlaceholder icon={Puzzle} title="Reading plugins" busy />
    {:else}
      <LightCoverage {coverage} />

      <section class="flex flex-col gap-3" aria-labelledby="plugins-installed">
        <h2 id="plugins-installed" class="font-heading text-base font-semibold text-text-primary">
          Installed <span class="ml-1 font-mono text-caption font-normal text-text-muted">{installed.length}</span>
        </h2>
        {#if installed.length === 0}
          <StatePlaceholder icon={Puzzle} title="No plugins installed" />
        {:else}
          <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            {#each installed as plugin (plugin.id)}
              <PluginLightCard {plugin} connections={connectionsByPlugin.get(plugin.id) ?? []} />
            {/each}
          </div>
        {/if}
      </section>

      {#if available.length > 0}
        <section class="flex flex-col gap-3" aria-labelledby="plugins-available">
          <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-1">
            <h2 id="plugins-available" class="font-heading text-base font-semibold text-text-primary">
              Community <span class="ml-1 font-mono text-caption font-normal text-text-muted">{available.length}</span>
            </h2>
            <a href="/plugins" class={buttonVariants({ variant: "ghost", size: "sm" })}>
              Install
              <ArrowRight aria-hidden="true" />
            </a>
          </div>
          <ul
            class="grid gap-px overflow-hidden rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-border-subtle)] lg:grid-cols-2 lg:[&>li:last-child:nth-child(odd)]:col-span-2"
          >
            {#each available as plugin (plugin.id)}
              <PluginAvailableRow {plugin} />
            {/each}
          </ul>
        </section>
      {/if}
    {/if}
  {/if}
</ManageConceptFrame>
