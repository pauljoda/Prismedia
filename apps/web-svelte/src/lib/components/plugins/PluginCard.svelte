<script lang="ts">
  import type { Snippet } from "svelte";
  import { Download, KeyRound, Loader2, Plug, Power, Trash2 } from "@lucide/svelte";
  import { Button, StatusLed, buttonVariants, cn, type LedStatus } from "@prismedia/ui-svelte";
  import { CONNECTION_STATUS } from "$lib/api/generated/codes";
  import type { ConnectionResponse, PluginProvider } from "$lib/api/generated/model";
  import { bandGradient } from "$lib/entities/media-families";
  import { connectionStatusLabels } from "$lib/integrations/connection-labels";
  import { pluginAttention, pluginBeam, pluginFamilies } from "$lib/plugins/plugin-families";
  import PluginIcon from "./PluginIcon.svelte";

  interface Props {
    plugin: PluginProvider;
    /** Connections backed by this plugin. */
    connections?: ConnectionResponse[];
    /** Whether the credential form below the card is open. */
    credentialsOpen?: boolean;
    updating?: boolean;
    removing?: boolean;
    enabling?: boolean;
    onUpdate?: (plugin: PluginProvider) => void;
    onRemove?: (plugin: PluginProvider) => void;
    onEnable?: (plugin: PluginProvider) => void;
    onToggleCredentials?: (plugin: PluginProvider) => void;
    /** The credential form, rendered inside the card while it is open. */
    credentials?: Snippet;
  }

  let {
    plugin,
    connections = [],
    credentialsOpen = false,
    updating = false,
    removing = false,
    enabling = false,
    onUpdate,
    onRemove,
    onEnable,
    onToggleCredentials,
    credentials,
  }: Props = $props();

  const families = $derived(pluginFamilies(plugin));
  const attention = $derived(pluginAttention(plugin, connections));
  const beam = $derived(pluginBeam(families));
  const hasCredentials = $derived(plugin.supports.length > 0 && plugin.auth.length > 0);
  const missingKeys = $derived(plugin.missingAuthKeys.length > 0);

  /** The LED for a connection that needs attention; a ready connection rests with no LED at all. */
  function connectionLed(connection: ConnectionResponse): LedStatus | null {
    if (!connection.enabled) return "idle";
    if (connection.status === CONNECTION_STATUS.ready) return null;
    if (connection.status === CONNECTION_STATUS.unverified) return "info";
    return "error";
  }
</script>

<!--
  A plugin as a light source: neutral light enters on the left of its top edge and leaves as the
  families of media it serves. The rows below name those families and what the plugin does in each.
-->
<article
  class="relative flex min-w-0 flex-col overflow-hidden rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-2)] shadow-[var(--shadow-card)]"
  aria-label={plugin.name}
>
  <span
    class={cn("absolute inset-x-0 top-0 h-px", plugin.enabled ? "opacity-80" : "opacity-30")}
    style:background={beam}
    aria-hidden="true"
  ></span>

  <header class={cn("flex items-start gap-3 px-4 pt-4", !plugin.enabled && "opacity-60")}>
    <PluginIcon name={plugin.name} iconUrl={plugin.iconUrl} class="size-11" />
    <div class="min-w-0 flex-1">
      <div class="flex min-w-0 flex-wrap items-baseline gap-x-2">
        <h3 class="truncate font-heading text-sm font-semibold text-text-primary">{plugin.name}</h3>
        <span class="font-mono text-[0.68rem] text-text-muted">v{plugin.version}</span>
      </div>
      {#if attention || plugin.updateAvailable}
        <p class="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-caption text-text-secondary">
          {#if attention}
            <span class="flex items-center gap-1.5">
              <StatusLed status={attention.led} size="sm" />
              {attention.label}
            </span>
          {/if}
          {#if plugin.updateAvailable && plugin.availableVersion}
            <span class="flex items-center gap-1.5 text-text-muted">
              <StatusLed status="info" size="sm" />
              v{plugin.availableVersion} available
            </span>
          {/if}
        </p>
      {/if}
    </div>
  </header>

  <ul class={cn("flex flex-1 flex-col gap-2 p-4", !plugin.enabled && "opacity-60")} aria-label="Media {plugin.name} serves">
    {#each families as support (support.family.key)}
      <li class="grid min-w-0 grid-cols-[1.75rem_minmax(0,6.5rem)_minmax(0,1fr)] items-center gap-2.5" title={support.kinds.join(", ")}>
        <span class="h-1.5 rounded-[2px]" style:background={bandGradient(support.family.accent)} aria-hidden="true"></span>
        <span class="truncate text-caption text-text-secondary">{support.family.label}</span>
        <span class="truncate font-mono text-[0.66rem] text-text-muted">{support.operations.join(" · ")}</span>
      </li>
    {:else}
      <li class="font-mono text-caption text-text-disabled">—</li>
    {/each}
  </ul>

  {#if plugin.integration}
    <div class="border-t border-[var(--color-border-subtle)] px-4 py-2.5">
      {#if connections.length === 0}
        <a
          href="/settings/connections"
          class="flex items-center gap-2 text-caption text-text-muted transition-colors hover:text-text-primary"
        >
          <Plug class="size-3.5" aria-hidden="true" />
          <span><span class="font-mono text-text-secondary">0</span> connections</span>
        </a>
      {:else}
        <ul class="flex flex-col gap-1" aria-label="Connections using {plugin.name}">
          {#each connections as connection (connection.id)}
            {@const led = connectionLed(connection)}
            <li>
              <a
                href="/settings/connections"
                class="flex min-w-0 items-center justify-between gap-3 text-caption transition-colors hover:text-text-primary"
              >
                <span class="flex min-w-0 items-center gap-2 text-text-secondary">
                  {#if led}
                    <StatusLed status={led} size="sm" />
                  {/if}
                  <span class="truncate">{connection.name}</span>
                </span>
                {#if !led}
                  <span class="sr-only">{connectionStatusLabels[connection.status]}</span>
                {:else}
                  <span class="shrink-0 text-text-muted">
                    {connection.enabled ? connectionStatusLabels[connection.status] : "Disabled"}
                  </span>
                {/if}
              </a>
            </li>
          {/each}
        </ul>
      {/if}
    </div>
  {/if}

  <footer class="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 border-t border-[var(--color-border-subtle)] py-1.5 pr-1.5 pl-4">
    <span class="min-w-0 truncate font-mono text-[0.64rem] text-text-disabled">{plugin.id}</span>
    <div class="ml-auto flex flex-wrap items-center gap-1">
      {#if plugin.updateAvailable && onUpdate}
        <Button variant="secondary" size="sm" disabled={updating} onclick={() => onUpdate(plugin)}>
          {#if updating}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Download aria-hidden="true" />{/if}
          Update
        </Button>
      {/if}
      {#if !plugin.enabled && onEnable}
        <Button variant="secondary" size="sm" disabled={enabling} onclick={() => onEnable(plugin)}>
          {#if enabling}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Power aria-hidden="true" />{/if}
          Enable
        </Button>
      {/if}
      {#if hasCredentials && onToggleCredentials}
        <Button
          variant={missingKeys && !credentialsOpen ? "secondary" : "ghost"}
          size="sm"
          aria-expanded={credentialsOpen}
          onclick={() => onToggleCredentials(plugin)}
        >
          <KeyRound aria-hidden="true" />
          {credentialsOpen ? "Close" : "Keys"}
        </Button>
      {/if}
      {#if plugin.integration}
        <a href="/settings/connections" class={buttonVariants({ variant: "ghost", size: "sm" })}>
          <Plug aria-hidden="true" />
          Connect
        </a>
      {/if}
      {#if onRemove}
        <Button
          variant="ghost"
          size="icon-sm"
          class="text-text-muted hover:text-error-text"
          disabled={removing}
          aria-label="Remove {plugin.name}"
          title="Remove"
          onclick={() => onRemove(plugin)}
        >
          {#if removing}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Trash2 aria-hidden="true" />{/if}
        </Button>
      {/if}
    </div>
  </footer>

  {#if credentialsOpen && credentials}
    {@render credentials()}
  {/if}
</article>
