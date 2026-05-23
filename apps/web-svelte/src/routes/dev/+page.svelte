<script lang="ts">
  import {
    Activity,
    Building2,
    Film,
    Grid2x2,
    Images,
    LayoutDashboard,
    LayoutList,
    Palette,
    Search,
    Settings,
    Users,
  } from "@lucide/svelte";
  import { resolve } from "$app/paths";
  import type { AppRouteId } from "$lib/app-routes";
  import type { Component } from "svelte";

  interface DevLink {
    label: string;
    href: AppRouteId;
    description: string;
    icon: Component<Record<string, unknown>>;
  }

  const shellLinks: DevLink[] = [
    {
      label: "Dashboard",
      href: "/",
      description: "Open the main shell landing route with API fallbacks.",
      icon: LayoutDashboard,
    },
    {
      label: "Videos",
      href: "/videos",
      description: "Exercise the primary media-list surface.",
      icon: Film,
    },
    {
      label: "Galleries",
      href: "/galleries",
      description: "Check the image-forward list surface.",
      icon: Images,
    },
    {
      label: "Search",
      href: "/search",
      description: "Open the global browse and query route.",
      icon: Search,
    },
    {
      label: "Settings",
      href: "/settings",
      description: "Review operational forms and panels.",
      icon: Settings,
    },
    {
      label: "Jobs",
      href: "/jobs",
      description: "Inspect the operations dashboard shell.",
      icon: Activity,
    },
  ];

  const labLinks: DevLink[] = [
    {
      label: "Design System",
      href: "/design-language",
      description: "Render Dark Room tokens and UI primitives.",
      icon: Palette,
    },
    {
      label: "Thumbnail Lab",
      href: "/dev/thumbnail-lab",
      description: "Preview shared entity grids with synthetic cards.",
      icon: Grid2x2,
    },
    {
      label: "Detail Lab",
      href: "/dev/detail-lab",
      description: "Preview entity detail layouts and controls.",
      icon: LayoutList,
    },
  ];

  const taxonomyLinks: DevLink[] = [
    {
      label: "People",
      href: "/people",
      description: "Open people thumbnails and filters.",
      icon: Users,
    },
    {
      label: "Studios",
      href: "/studios",
      description: "Open studio thumbnails and filters.",
      icon: Building2,
    },
  ];

</script>

<svelte:head>
  <title>Dev Shim | Prismedia</title>
  <meta name="robots" content="noindex,nofollow" />
</svelte:head>

<main class="dev-shim">
  <header class="hero">
    <p>Developer doorway</p>
    <h1>Web Dev Shim</h1>
    <span>Fast links for checking the app shell and design labs while backend work is in flux.</span>
  </header>

  <section class="link-section" aria-labelledby="shell-heading">
    <div class="section-heading">
      <h2 id="shell-heading">Main Shell</h2>
      <p>Routes that exercise the normal chrome, navigation, breadcrumbs, and empty/error states.</p>
    </div>
    <div class="link-grid">
      {#each shellLinks as item (item.href)}
        <a class="shim-link" href={resolve(item.href as "/")}>
          <item.icon class="h-4 w-4" />
          <span>
            <strong>{item.label}</strong>
            <small>{item.description}</small>
          </span>
        </a>
      {/each}
    </div>
  </section>

  <section class="link-section" aria-labelledby="labs-heading">
    <div class="section-heading">
      <h2 id="labs-heading">Design Labs</h2>
      <p>Synthetic, backend-free surfaces for visual inspection.</p>
    </div>
    <div class="link-grid">
      {#each labLinks as item (item.href)}
        <a class="shim-link" href={resolve(item.href as "/")}>
          <item.icon class="h-4 w-4" />
          <span>
            <strong>{item.label}</strong>
            <small>{item.description}</small>
          </span>
        </a>
      {/each}
    </div>
  </section>

  <section class="link-section" aria-labelledby="taxonomy-heading">
    <div class="section-heading">
      <h2 id="taxonomy-heading">Taxonomy</h2>
      <p>Useful browse pages for checking compact entity treatments.</p>
    </div>
    <div class="link-grid">
      {#each taxonomyLinks as item (item.href)}
        <a class="shim-link" href={resolve(item.href as "/")}>
          <item.icon class="h-4 w-4" />
          <span>
            <strong>{item.label}</strong>
            <small>{item.description}</small>
          </span>
        </a>
      {/each}
    </div>
  </section>
</main>

<style>
  .dev-shim {
    display: grid;
    gap: 1.4rem;
    color: var(--color-text-primary, #f2eed8);
    padding-bottom: 3rem;
  }

  .hero {
    border: 1px solid var(--color-border-subtle, #1c2235);
    background:
      linear-gradient(135deg, rgb(196 154 90 / 0.11), transparent 36%),
      var(--color-surface-1, #0c0f15);
    padding: clamp(1.1rem, 4vw, 2rem);
  }

  .hero p,
  .section-heading p,
  .shim-link small {
    margin: 0;
    color: var(--color-text-muted, #8a93a6);
  }

  .hero p,
  .section-heading p {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
    letter-spacing: 0;
    text-transform: uppercase;
  }

  .hero h1 {
    margin: 0.3rem 0 0.55rem;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: clamp(2rem, 7vw, 4.5rem);
    line-height: 0.96;
    letter-spacing: 0;
  }

  .hero span {
    display: block;
    max-width: 42rem;
    color: var(--color-text-secondary, #c4c9d4);
    font-size: 0.98rem;
    line-height: 1.55;
  }

  .link-section {
    display: grid;
    gap: 0.75rem;
  }

  .section-heading {
    display: flex;
    align-items: end;
    justify-content: space-between;
    gap: 1rem;
  }

  .section-heading h2 {
    margin: 0;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 1.25rem;
    letter-spacing: 0;
  }

  .section-heading p {
    text-align: right;
  }

  .link-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(min(100%, 16rem), 1fr));
    gap: 0.75rem;
  }

  .shim-link {
    display: grid;
    grid-template-columns: auto 1fr;
    gap: 0.75rem;
    min-height: 5.2rem;
    border: 1px solid var(--color-border-subtle, #1c2235);
    background: var(--color-surface-1, #0c0f15);
    color: var(--color-text-primary, #f2eed8);
    padding: 0.9rem;
    text-decoration: none;
    transition: border-color 0.15s ease, box-shadow 0.15s ease, transform 0.15s ease;
  }

  .shim-link:hover,
  .shim-link:focus-visible {
    border-color: var(--color-border-accent, #5a4620);
    box-shadow: 0 0 18px rgb(196 154 90 / 0.18);
    outline: none;
    transform: translateY(-1px);
  }

  .shim-link :global(svg) {
    color: var(--color-text-accent, #c49a5a);
    filter: drop-shadow(0 0 8px rgb(196 154 90 / 0.4));
    margin-top: 0.12rem;
  }

  .shim-link span {
    display: grid;
    gap: 0.28rem;
    min-width: 0;
  }

  .shim-link strong {
    font-size: 0.96rem;
    line-height: 1.2;
  }

  .shim-link small {
    font-size: 0.78rem;
    line-height: 1.42;
  }

  @media (max-width: 640px) {
    .section-heading {
      align-items: start;
      flex-direction: column;
    }

    .section-heading p {
      text-align: left;
    }
  }
</style>
