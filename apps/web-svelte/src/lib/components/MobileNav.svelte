<script lang="ts">
  import { Film, FolderTree, Images, Users } from "@lucide/svelte";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import type { Component } from "svelte";
  import { cn } from "@prismedia/ui-svelte";
  import type { AppRouteId } from "$lib/app-routes";
  import MobileMoreSheet from "./MobileMoreSheet.svelte";
  import MobileMoreNavButton from "./MobileMoreNavButton.svelte";
  import { appShellSections } from "./app-shell-sections";

  interface PrimaryTab {
    label: string;
    href: AppRouteId;
    icon: Component<Record<string, unknown>>;
  }

  const primaryTabs: PrimaryTab[] = [
    { label: "Files", href: "/files", icon: FolderTree },
    { label: "Videos", href: "/videos", icon: Film },
    { label: "Galleries", href: "/galleries", icon: Images },
    { label: "People", href: "/people", icon: Users },
  ];

  const primaryHrefs = new Set(primaryTabs.map((tab) => tab.href));
  const moreRoutes = appShellSections
    .flatMap((section) => section.items.map((item) => item.href))
    .filter((href) => !primaryHrefs.has(href));

  const pathname = $derived(page.url.pathname);
  let sheetOpen = $state(false);
  const isMoreActive = $derived(
    sheetOpen ||
      moreRoutes.some((route) => pathname === route || (route !== "/" && pathname.startsWith(route + "/"))),
  );

</script>

<nav
  class="fixed bottom-0 left-0 right-0 z-50 flex h-14 items-center justify-around border-t border-border-subtle bg-surface-1 md:hidden"
>
  {#each primaryTabs as tab (tab.href)}
    {@const active = pathname === tab.href || pathname.startsWith(tab.href + "/")}
    {@const Icon = tab.icon}
    <a
      href={resolve(tab.href as "/")}
      aria-current={active ? "page" : undefined}
      class={cn(
        "flex flex-col items-center gap-0.5 px-3 py-1.5 text-[0.65rem] transition-colors duration-fast",
        active ? "text-text-accent" : "text-text-disabled hover:text-text-muted",
      )}
    >
      <Icon class="h-5 w-5" />
      <span>{tab.label}</span>
    </a>
  {/each}

  <MobileMoreNavButton
    {isMoreActive}
    {sheetOpen}
    onToggleSheet={() => (sheetOpen = !sheetOpen)}
  />
</nav>

<MobileMoreSheet open={sheetOpen} onClose={() => (sheetOpen = false)} />
