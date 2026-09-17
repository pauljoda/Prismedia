<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { replaceState } from "$app/navigation";
  import { Activity, Compass, Link, Send, Settings } from "@lucide/svelte";
  import { Alert, Tabs, buttonVariants } from "@prismedia/ui-svelte";
  import type { ConnectionResponse } from "$lib/api/generated/model";
  import { fetchConnections } from "$lib/api/connections";
  import RequestDiscover from "$lib/components/requests/RequestDiscover.svelte";
  import RequestActivity from "$lib/components/requests/RequestActivity.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import { useSession } from "$lib/stores/session.svelte";

  const session = useSession();
  // These are view labels, not server state codes.
  const browseTab = "Browse";
  const activityTab = "Activity";
  let activeTab = $state(browseTab);
  $effect(() => { activeTab = page.url.searchParams.has("activity") && session.isAdmin ? activityTab : browseTab; });
  let connections = $state<ConnectionResponse[]>([]);
  let loaded = $state(false);
  let error = $state<string | null>(null);
  onMount(() => {
    if (!session.isAdmin) { loaded = true; return; }
    let alive = true;
    void fetchConnections().then(value => { if (alive) connections = value; })
      .catch(cause => { if (alive) error = cause instanceof Error ? cause.message : "Could not load connected sources"; })
      .finally(() => { if (alive) loaded = true; });
    return () => { alive = false; };
  });
  function chooseTab(value: string) {
    activeTab = value;
    const url = new URL(window.location.href);
    if (value === activityTab) url.searchParams.set("activity", ""); else url.searchParams.delete("activity");
    replaceState(url, page.state);
  }
  function chooseConnection(id: string | null) {
    const url = new URL(window.location.href);
    if (id) url.searchParams.set("connection", id); else url.searchParams.delete("connection");
    replaceState(url, page.state);
  }
</script>

<svelte:head><title>Request · Prismedia</title></svelte:head>
{#if !session.canRequestContent}
  <StatePlaceholder icon={Send} title="Request access required" description="Ask an administrator to allow content requests for your account." />
{:else}
  <div class="space-y-5">
    <header class="flex flex-wrap items-center justify-between gap-4">
      <div><h1>Request</h1><p class="mt-1 text-sm text-text-muted">Find something to add to your library.</p></div>
      {#if session.isAdmin}<div class="flex gap-2">
        <a class={buttonVariants({ variant: "secondary", size: "sm" })} href="/request/urls"><Link />Add from URL</a>
        <a class={buttonVariants({ variant: "ghost", size: "sm" })} href="/settings/connections"><Settings />Sources</a>
      </div>{/if}
    </header>
    {#if error}<Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>{/if}
    <Tabs.Root value={activeTab} onValueChange={chooseTab}>
      <Tabs.List variant="line" aria-label="Request workspace">
        <Tabs.Trigger value={browseTab}><Compass />Browse</Tabs.Trigger>
        {#if session.isAdmin}<Tabs.Trigger value={activityTab}><Activity />Activity</Tabs.Trigger>{/if}
      </Tabs.List>
      <Tabs.Content value={browseTab} class={activeTab === browseTab ? "pt-5" : "hidden"}>
        {#if loaded}<RequestDiscover {connections} initialConnectionId={page.url.searchParams.get("connection")} onConnectionChange={chooseConnection} />
        {:else}<StatePlaceholder icon={Compass} title="Loading sources" busy />{/if}
      </Tabs.Content>
      {#if session.isAdmin}<Tabs.Content value={activityTab} class="pt-5">{#if activeTab === activityTab}<RequestActivity {connections} />{/if}</Tabs.Content>{/if}
    </Tabs.Root>
  </div>
{/if}
