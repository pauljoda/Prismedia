<script lang="ts">
  import {
    Badge,
    Button,
    Checkbox,
    Meter,
    Panel,
    Select,
    StatusLed,
    TextInput,
    Toggle,
    type LedStatus,
    type SelectOption,
  } from "@prismedia/ui-svelte";
  import LogoMark from "$lib/components/LogoMark.svelte";

  const ledStatuses: LedStatus[] = [
    "active",
    "warning",
    "error",
    "info",
    "idle",
    "accent",
    "phosphor",
  ];

  let demoChecked = $state(false);
  let demoIndeterminate = $state(true);
  let demoToggle = $state(false);
  let demoToggleSm = $state(true);
  let demoSelectValue = $state("");

  const demoSelectOptions: SelectOption[] = [
    { value: "option-a", label: "Option A" },
    { value: "option-b", label: "Option B" },
    { value: "option-c", label: "Option C" },
    { value: "option-d", label: "Option D (disabled)", disabled: true },
  ];

  const corePalette = [
    { label: "Noir", hex: "#07080b", token: "bg" },
    { label: "Obsidian", hex: "#0b0e12", token: "surface-1" },
    { label: "Graphite", hex: "#11161d", token: "surface-2" },
    { label: "Slate Glass", hex: "#202734", token: "surface-3" },
    { label: "Carbon", hex: "#2a3038", token: "surface-4" },
  ];

  const accentPalette = [
    { label: "Amber", hex: "#d59a2a", token: "600" },
    { label: "Brass", hex: "#f2c26a", token: "500" },
    { label: "Ivory", hex: "#f0ede3", token: "50" },
    { label: "Mist", hex: "#bbb0a2", token: "phosphor" },
    { label: "Muted Blue", hex: "#3b475c", token: "muted-blue" },
  ];

  const accentScale = [
    { label: "950", hex: "#1a1408" },
    { label: "900", hex: "#2d2210" },
    { label: "800", hex: "#4a3818" },
    { label: "700", hex: "#7a5e20" },
    { label: "600", hex: "#d59a2a" },
    { label: "500", hex: "#f2c26a" },
    { label: "400", hex: "#f5d48a" },
    { label: "300", hex: "#f7dfa0" },
    { label: "200", hex: "#faecc0" },
    { label: "100", hex: "#fdf5e0" },
    { label: "50", hex: "#fefaf0" },
  ];

  const signalColors = [
    { label: "Success", hex: "#63c889", text: "#8ee0aa", token: "success" },
    { label: "Info", hex: "#6fa8dc", text: "#92c0e8", token: "info" },
    { label: "Warning", hex: "#f2c26a", text: "#f5d48a", token: "warning" },
    { label: "Danger", hex: "#ff806f", text: "#ff9f92", token: "error" },
  ];

  const textColors = [
    { label: "Primary", css: "text-text-primary", hex: "#f0ede3" },
    { label: "Secondary", css: "text-text-secondary", hex: "#c8ccd4" },
    { label: "Muted", css: "text-text-muted", hex: "#a4acb9" },
    { label: "Disabled", css: "text-text-disabled", hex: "#5a6070" },
    { label: "Accent", css: "text-text-accent", hex: "#f2c26a" },
  ];

  const surfaceLayers = [
    {
      layer: "Layer 0",
      name: "Canvas",
      desc: "Root app background. Deepest level.",
      css: "var(--color-bg)",
      border: "—",
      shadow: "—",
    },
    {
      layer: "Layer 1",
      name: "Material Panel",
      desc: "Structural layout. Mostly opaque.",
      css: "var(--color-surface-1)",
      border: "rgba(164,172,185,0.06)",
      shadow: "inset 0 1px, drop 4px 24px",
    },
    {
      layer: "Layer 2",
      name: "Glass Card",
      desc: "Interactive content. Elevated & blurred.",
      css: "var(--color-surface-2)",
      border: "rgba(164,172,185,0.12)",
      shadow: "0 2px 12px",
    },
    {
      layer: "Layer 3",
      name: "Signal Glass",
      desc: "Active / Selected / Focus. Premium emphasis.",
      css: "var(--color-surface-3)",
      border: "rgba(242,194,106,0.24)",
      shadow: "glow 16px + 32px ambient",
    },
  ];

  const glassLevels = [
    { label: "glass-1", opacity: "72%", blur: "12px" },
    { label: "glass-2", opacity: "82%", blur: "16px" },
    { label: "glass-3", opacity: "92%", blur: "24px" },
  ];

  const statusBadges = [
    { label: "Running", color: "success" as const },
    { label: "Queued", color: "warning" as const },
    { label: "Completed", color: "success" as const },
    { label: "Failed", color: "error" as const },
    { label: "Warning", color: "warning" as const },
    { label: "Paused", color: "info" as const },
  ];

  const metaBadges = ["4K", "HDR", "HEVC", "Dolby Vision", "7.1", "SRT"];

  const easingCurves = [
    { name: "Default", value: "cubic-bezier(0.4, 0, 0.2, 1)", token: "--ease-default" },
    { name: "Mechanical", value: "cubic-bezier(0.25, 0, 0.25, 1)", token: "--ease-mechanical" },
    { name: "Enter", value: "cubic-bezier(0, 0, 0.2, 1)", token: "--ease-enter" },
    { name: "Exit", value: "cubic-bezier(0.4, 0, 1, 1)", token: "--ease-exit" },
  ];

  const durations = [
    { name: "Fast", value: "100ms", token: "--duration-fast" },
    { name: "Normal", value: "180ms", token: "--duration-normal" },
    { name: "Moderate", value: "250ms", token: "--duration-moderate" },
    { name: "Slow", value: "400ms", token: "--duration-slow" },
  ];
</script>

<svelte:head>
  <title>Design Language — Prismedia</title>
  <meta name="robots" content="noindex,nofollow" />
</svelte:head>

<main class="min-h-screen bg-bg text-text-primary">
  <!-- ═══════════════════════════════ HERO HEADER ═══════════════════════════════ -->
  <header class="ds-hero">
    <div class="ds-hero-inner">
      <div class="ds-hero-brand">
        <LogoMark size={72} />
        <div>
          <h1 class="ds-hero-title font-display">PRISMEDIA</h1>
          <p class="ds-hero-subtitle">PRISM NOIR LUXE</p>
          <p class="ds-hero-version">Global Design System ·.0</p>
        </div>
      </div>

      <div class="ds-hero-tagline">
        <p class="ds-hero-tagline-text">
          Private. Self-Hosted. Cinematic control for your universe.
        </p>
        <p class="text-body-sm text-text-muted max-w-md">
          A cohesive design language built for clarity, control, and confidence across Prismedia.
        </p>
      </div>

      <div class="ds-hero-pillars">
        {#each [
          { icon: "🔒", label: "PRIVATE", desc: "Your data stays on your server." },
          { icon: "🖥️", label: "SELF-HOSTED", desc: "Full control. No compromises." },
          { icon: "🔌", label: "EXTENSIBLE", desc: "Plugins, scripts, automations." },
          { icon: "◈", label: "PRECISION", desc: "Every surface, every detail." },
        ] as pillar}
          <div class="ds-pillar">
            <span class="ds-pillar-icon">{pillar.icon}</span>
            <span class="ds-pillar-label">{pillar.label}</span>
            <span class="ds-pillar-desc">{pillar.desc}</span>
          </div>
        {/each}
      </div>
    </div>

    <div class="ds-hero-divider"></div>
  </header>

  <div class="mx-auto max-w-[1400px] px-6 pb-16 space-y-16">
    <a href="/" class="inline-flex items-center gap-1 text-mono-sm text-text-muted hover:text-text-accent transition-colors">
      ← Back to Dashboard
    </a>

    <!-- ═══════════════════════════════ COLOR PALETTE ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">COLOR PALETTE</h2>
      </div>

      <div class="grid lg:grid-cols-3 gap-8">
        <!-- Core -->
        <div class="space-y-3">
          <h3 class="text-kicker">CORE</h3>
          <div class="grid grid-cols-5 gap-2">
            {#each corePalette as swatch}
              <div class="flex flex-col items-center gap-2">
                <div
                  class="ds-swatch"
                  style="background: {swatch.hex}"
                ></div>
                <span class="text-mono-sm text-text-muted text-center leading-tight">{swatch.label}</span>
                <span class="text-mono-sm text-text-disabled">{swatch.hex}</span>
              </div>
            {/each}
          </div>
        </div>

        <!-- Accent -->
        <div class="space-y-3">
          <h3 class="text-kicker">ACCENT</h3>
          <div class="grid grid-cols-5 gap-2">
            {#each accentPalette as swatch}
              <div class="flex flex-col items-center gap-2">
                <div
                  class="ds-swatch"
                  style="background: {swatch.hex}; box-shadow: 0 0 12px {swatch.hex}33"
                ></div>
                <span class="text-mono-sm text-text-muted text-center leading-tight">{swatch.label}</span>
                <span class="text-mono-sm text-text-disabled">{swatch.hex}</span>
              </div>
            {/each}
          </div>
        </div>

        <!-- Signal -->
        <div class="space-y-3">
          <h3 class="text-kicker">SIGNAL</h3>
          <div class="grid grid-cols-4 gap-2">
            {#each signalColors as signal}
              <div class="flex flex-col items-center gap-2">
                <div
                  class="ds-swatch ds-swatch-signal"
                  style="background: {signal.hex}; box-shadow: 0 0 10px {signal.hex}66"
                >
                  <div class="w-2.5 h-2.5" style="background: {signal.text}"></div>
                </div>
                <span class="text-mono-sm" style="color: {signal.text}">{signal.label}</span>
                <span class="text-mono-sm text-text-disabled">{signal.hex}</span>
              </div>
            {/each}
          </div>
        </div>
      </div>

      <!-- Full accent ramp -->
      <div class="mt-8 space-y-3">
        <h3 class="text-kicker">BRASS ACCENT RAMP</h3>
        <div class="flex">
          {#each accentScale as swatch}
            <div
              class="flex-1 h-12 flex flex-col items-center justify-end pb-1"
              style="background: {swatch.hex}"
            >
              <span class="text-mono-sm" style="color: {parseInt(swatch.label) >= 500 ? '#131008' : '#f2eed8'}">{swatch.label}</span>
            </div>
          {/each}
        </div>
      </div>
    </section>

    <!-- ═══════════════════════════════ TYPOGRAPHY ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">TYPOGRAPHY</h2>
      </div>

      <div class="grid lg:grid-cols-[1fr_1fr] gap-8">
        <!-- Font families -->
        <div class="space-y-6">
          <div class="ds-type-sample">
            <span class="ds-type-label">Cinzel</span>
            <span class="ds-type-role">Display / Brand</span>
            <p class="font-display text-4xl font-bold tracking-wider leading-none mt-3">Aa</p>
            <p class="font-display text-lg text-text-secondary mt-1">Cinematic headings</p>
          </div>

          <div class="ds-type-sample">
            <span class="ds-type-label">Geist Sans</span>
            <span class="ds-type-role">Product Headings</span>
            <p class="font-heading text-4xl font-bold tracking-tighter leading-none mt-3">Aa</p>
            <p class="font-heading text-lg font-semibold tracking-tight text-text-secondary mt-1">Section headings</p>
          </div>

          <div class="ds-type-sample">
            <span class="ds-type-label">Inter Variable</span>
            <span class="ds-type-role">Body</span>
            <p class="font-body text-4xl font-normal leading-none mt-3">Aa</p>
            <p class="font-body text-text-secondary mt-1">Primary UI copy</p>
          </div>

          <div class="ds-type-sample">
            <span class="ds-type-label">JetBrains Mono</span>
            <span class="ds-type-role">Metadata / Code</span>
            <p class="font-mono text-3xl leading-none mt-3">Aa</p>
            <p class="font-mono text-sm text-text-secondary mt-1">Operational details</p>
          </div>
        </div>

        <!-- Scale -->
        <Panel>
          <div class="p-5 space-y-3">
            <p class="text-display">Display · Prismedia</p>
            <p class="text-h1">H1 · Dark Room</p>
            <p class="text-h2">H2 · Brass &amp; Glass</p>
            <p class="text-h3">H3 · Sharp corners everywhere</p>
            <p class="text-body">Body · The quick brown fox jumps over the lazy dog.</p>
            <p class="text-body-sm text-text-secondary">Body sm · Secondary metadata and supplemental text.</p>
            <p class="text-label text-text-muted uppercase">Label · tracking 0.04em</p>
            <p class="text-kicker">Kicker · tracking 0.15em</p>
            <p class="text-mono text-text-accent">Mono · 0x2a4f00 // <span class="text-phosphor-500">phosphor</span></p>
            <p class="text-mono-sm text-text-muted">Mono sm · timestamps, file sizes, technical data</p>
            <div class="pt-2 border-t border-border-subtle">
              <p class="text-glow-accent font-heading text-lg">Glow accent · active state text</p>
              <p class="text-glow-phosphor font-heading text-lg mt-1">Glow phosphor · digital signal</p>
            </div>
          </div>
        </Panel>
      </div>

      <!-- Text colors -->
      <div class="mt-8">
        <h3 class="text-kicker mb-3">TEXT COLORS</h3>
        <div class="flex flex-wrap gap-6">
          {#each textColors as tc}
            <div class="flex items-center gap-3">
              <div class="w-5 h-5 border border-border-subtle rounded-xs" style="background: {tc.hex}"></div>
              <div>
                <span class={tc.css}>{tc.label}</span>
                <span class="text-mono-sm text-text-disabled block">{tc.hex}</span>
              </div>
            </div>
          {/each}
        </div>
      </div>
    </section>

    <!-- ═══════════════════════════════ SURFACE SYSTEM ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">SURFACE SYSTEM</h2>
        <p class="ds-section-desc">Layered materials built for depth, hierarchy, and interaction.</p>
      </div>

      <div class="grid md:grid-cols-2 xl:grid-cols-4 gap-4">
        {#each surfaceLayers as layer, i}
          <div
            class="ds-surface-card"
            class:ds-surface-signal={i === 3}
            style="background: {layer.css}"
          >
            <div class="ds-surface-card-header">
              <span class="text-kicker">{layer.layer}</span>
              <span class="font-heading text-lg text-text-primary"
                class:text-text-accent={i === 3}
              >{layer.name}</span>
            </div>
            <p class="text-body-sm text-text-muted mt-2">{layer.desc}</p>
            <div class="ds-surface-card-props">
              <div>
                <span class="text-mono-sm text-text-disabled">Border</span>
                <span class="text-mono-sm text-text-muted block">{layer.border}</span>
              </div>
              <div>
                <span class="text-mono-sm text-text-disabled">Shadow</span>
                <span class="text-mono-sm text-text-muted block">{layer.shadow}</span>
              </div>
            </div>
          </div>
        {/each}
      </div>

      <!-- Glass layers -->
      <div class="mt-8 space-y-3">
        <h3 class="text-kicker">GLASS LAYERS</h3>
        <div class="relative h-48 overflow-hidden border border-border-subtle rounded-md">
          <div class="absolute inset-0 bg-gradient-to-br from-accent-800 via-accent-950 to-surface-1"></div>
          <div class="absolute inset-0 flex items-center justify-center gap-4 p-4">
            {#each glassLevels as glass}
              <div
                class="flex-1 h-full flex flex-col items-center justify-center border border-border-subtle rounded-sm"
                style="background: var(--color-overlay-glass); backdrop-filter: blur({glass.blur})"
              >
                <span class="text-mono text-text-primary">{glass.label}</span>
                <span class="text-mono-sm text-text-muted">{glass.opacity} · {glass.blur}</span>
              </div>
            {/each}
          </div>
        </div>
      </div>
    </section>

    <!-- ═══════════════════════════════ SPACING & RADIUS ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">SPACING &amp; RADIUS</h2>
      </div>

      <div class="grid lg:grid-cols-2 gap-8">
        <!-- Spacing scale -->
        <div class="space-y-3">
          <h3 class="text-kicker">SPACING SCALE (8pt)</h3>
          <Panel>
            <div class="p-5 flex flex-wrap items-end gap-3">
              {#each [4, 8, 12, 16, 20, 24, 32, 40, 48, 64] as size}
                <div class="flex flex-col items-center gap-1">
                  <div
                    class="bg-accent-500/30 border border-accent-500/40"
                    style="width: {size}px; height: {size}px;"
                  ></div>
                  <span class="text-mono-sm text-text-disabled">{size}</span>
                </div>
              {/each}
            </div>
          </Panel>
        </div>

        <!-- Radius scale -->
        <div class="space-y-3">
          <h3 class="text-kicker">RADII</h3>
          <Panel>
            <div class="p-5 flex flex-wrap items-end gap-4">
              {#each [
                { label: "0", value: "0px" },
                { label: "XS", value: "4px" },
                { label: "SM", value: "6px" },
                { label: "MD", value: "10px" },
                { label: "LG", value: "14px" },
                { label: "XL", value: "18px" },
                { label: "2XL", value: "24px" },
                { label: "Pill", value: "9999px" },
              ] as r}
                <div class="flex flex-col items-center gap-1">
                  <div
                    class="w-12 h-12 bg-surface-3 border border-border-default"
                    style="border-radius: {r.value};"
                  ></div>
                  <span class="text-mono-sm text-text-muted">{r.value}</span>
                  <span class="text-mono-sm text-text-disabled">{r.label}</span>
                </div>
              {/each}
            </div>
          </Panel>
        </div>
      </div>
    </section>

    <!-- ═══════════════════════════════ BUTTONS ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">BUTTONS</h2>
      </div>

      <div class="grid lg:grid-cols-3 gap-8">
        <!-- Variants -->
        <div class="space-y-3">
          <h3 class="text-kicker">VARIANTS</h3>
          <Panel>
            <div class="p-5 flex flex-col gap-3">
              <Button variant="primary">Primary Button</Button>
              <Button variant="secondary">Secondary</Button>
              <Button variant="ghost">Ghost Button</Button>
              <Button variant="danger">Danger</Button>
              <Button variant="primary" disabled>Disabled</Button>
            </div>
          </Panel>
        </div>

        <!-- Sizes -->
        <div class="space-y-3">
          <h3 class="text-kicker">SIZES</h3>
          <Panel>
            <div class="p-5 flex flex-col gap-3 items-start">
              <Button variant="primary" size="sm">Small</Button>
              <Button variant="primary">Medium</Button>
              <Button variant="primary" size="lg">Large</Button>
            </div>
          </Panel>
        </div>

        <!-- Icon actions -->
        <div class="space-y-3">
          <h3 class="text-kicker">ICON ACTIONS</h3>
          <Panel>
            <div class="p-5 flex flex-wrap gap-3">
              <button class="btn-accent px-3 py-2 text-lg">▶</button>
              <button class="btn-accent px-3 py-2 text-lg">♡</button>
              <button class="btn-accent px-3 py-2 text-lg">⬇</button>
              <button class="btn-accent px-3 py-2 text-lg text-text-muted !border-border-subtle !bg-surface-2">⋯</button>
            </div>
          </Panel>
        </div>
      </div>
    </section>

    <!-- ═══════════════════════════════ CHIPS & BADGES ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">CHIPS &amp; BADGES</h2>
      </div>

      <div class="grid lg:grid-cols-3 gap-8">
        <!-- Category chips -->
        <div class="space-y-3">
          <h3 class="text-kicker">CHIPS</h3>
          <Panel>
            <div class="p-5 flex flex-wrap gap-2">
              {#each ["Movies", "TV Shows", "Books", "Audio", "Images"] as chip}
                <span class="tag-chip tag-chip-default px-3 py-1.5">{chip}</span>
              {/each}
            </div>
          </Panel>
        </div>

        <!-- Status badges -->
        <div class="space-y-3">
          <h3 class="text-kicker">STATUS BADGES</h3>
          <Panel>
            <div class="p-5 flex flex-wrap gap-2">
              {#each statusBadges as badge}
                <Badge variant={badge.color}>{badge.label}</Badge>
              {/each}
            </div>
          </Panel>
        </div>

        <!-- Meta badges -->
        <div class="space-y-3">
          <h3 class="text-kicker">META BADGES</h3>
          <Panel>
            <div class="p-5 flex flex-wrap gap-2">
              {#each metaBadges as meta}
                <span class="pill-accent px-2 py-0.5 text-mono-sm font-semibold">{meta}</span>
              {/each}
            </div>
          </Panel>
        </div>
      </div>

      <!-- Tag color variants -->
      <div class="mt-6 space-y-3">
        <h3 class="text-kicker">TAG VARIANTS</h3>
        <Panel>
          <div class="p-5 flex flex-wrap gap-2">
            <span class="tag-chip tag-chip-default px-2 py-0.5">default</span>
            <span class="tag-chip tag-chip-accent px-2 py-0.5">accent</span>
            <span class="tag-chip tag-chip-info px-2 py-0.5">info</span>
            <span class="tag-chip tag-chip-success px-2 py-0.5">success</span>
            <span class="tag-chip tag-chip-warning px-2 py-0.5">warning</span>
            <span class="tag-chip tag-chip-error px-2 py-0.5">error</span>
          </div>
        </Panel>
      </div>
    </section>

    <!-- ═══════════════════════════════ FORMS & INPUTS ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">FORMS &amp; INPUTS</h2>
      </div>

      <div class="grid lg:grid-cols-2 gap-8">
        <!-- Text Input -->
        <div class="space-y-4">
          <h3 class="text-kicker">TEXT INPUT</h3>
          <Panel>
            <div class="p-5 space-y-4">
              <div>
                <label class="control-label">Default</label>
                <TextInput placeholder="Search Prismedia or type a command..." />
              </div>
              <div class="grid grid-cols-3 gap-3">
                <div>
                  <label class="control-label">Small</label>
                  <TextInput size="sm" placeholder="Small" />
                </div>
                <div>
                  <label class="control-label">Medium</label>
                  <TextInput placeholder="Medium" />
                </div>
                <div>
                  <label class="control-label">Large</label>
                  <TextInput size="lg" placeholder="Large" />
                </div>
              </div>
              <div>
                <label class="control-label">Error</label>
                <TextInput variant="error" value="Invalid value" />
              </div>
              <div>
                <label class="control-label">Disabled</label>
                <TextInput disabled value="Cannot edit" />
              </div>
            </div>
          </Panel>
        </div>

        <!-- Select -->
        <div class="space-y-4">
          <h3 class="text-kicker">DROPDOWN</h3>
          <Panel>
            <div class="p-5 space-y-4">
              <div>
                <label class="control-label">Default</label>
                <Select
                  options={demoSelectOptions}
                  bind:value={demoSelectValue}
                  placeholder="Select an option..."
                />
              </div>
              <div class="grid grid-cols-3 gap-3">
                <div>
                  <label class="control-label">Small</label>
                  <Select options={demoSelectOptions} size="sm" placeholder="Small" />
                </div>
                <div>
                  <label class="control-label">Medium</label>
                  <Select options={demoSelectOptions} placeholder="Medium" />
                </div>
                <div>
                  <label class="control-label">Large</label>
                  <Select options={demoSelectOptions} size="lg" placeholder="Large" />
                </div>
              </div>
              <div>
                <label class="control-label">Disabled</label>
                <Select options={demoSelectOptions} disabled value="option-a" />
              </div>
            </div>
          </Panel>
        </div>
      </div>

      <div class="grid lg:grid-cols-2 gap-8 mt-8">
        <!-- Checkbox -->
        <div class="space-y-4">
          <h3 class="text-kicker">CHECKBOX</h3>
          <Panel>
            <div class="p-5 space-y-3">
              <label class="flex items-center gap-2 text-body">
                <Checkbox checked />
                Checked
              </label>
              <label class="flex items-center gap-2 text-body">
                <Checkbox
                  checked={demoChecked}
                  onchange={(e) => (demoChecked = (e.currentTarget as HTMLInputElement).checked)}
                />
                Unchecked
              </label>
              <label class="flex items-center gap-2 text-body">
                <Checkbox indeterminate={demoIndeterminate} />
                Indeterminate
              </label>
              <label class="flex items-center gap-2 text-body text-text-disabled">
                <Checkbox disabled />
                Disabled
              </label>
            </div>
          </Panel>
        </div>

        <!-- Toggle -->
        <div class="space-y-4">
          <h3 class="text-kicker">TOGGLE</h3>
          <Panel>
            <div class="p-5 space-y-4">
              <div class="flex items-center justify-between">
                <span class="text-body">Enable notifications</span>
                <Toggle checked={demoToggle} onchange={(v) => (demoToggle = v)} />
              </div>
              <div class="flex items-center justify-between">
                <span class="text-body">Auto-scan on startup</span>
                <Toggle checked={demoToggleSm} onchange={(v) => (demoToggleSm = v)} size="sm" />
              </div>
              <div class="flex items-center justify-between">
                <span class="text-body text-text-disabled">Disabled toggle</span>
                <Toggle disabled />
              </div>
              <div class="pt-3 border-t border-border-subtle">
                <label class="control-label">Meter</label>
                <Meter value={64} showValue />
              </div>
            </div>
          </Panel>
        </div>
      </div>
    </section>

    <!-- ═══════════════════════════════ INTERACTION STATES ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">INTERACTION STATES</h2>
      </div>

      <div class="grid grid-cols-2 md:grid-cols-5 gap-4">
        <!-- Default -->
        <div class="flex flex-col items-center gap-3">
          <div class="ds-state-card surface-card-sharp">
            <div class="ds-state-thumb gradient-thumb-2"></div>
            <div class="p-3">
              <p class="text-body-sm font-medium truncate">Interstellar</p>
              <p class="text-mono-sm text-text-muted">2014 · 2h 49m</p>
            </div>
          </div>
          <div class="text-center">
            <span class="text-label text-text-primary block">Default</span>
            <span class="text-mono-sm text-text-disabled">Resting state.</span>
          </div>
        </div>

        <!-- Hover -->
        <div class="flex flex-col items-center gap-3">
          <div class="ds-state-card surface-card-sharp no-lift" style="border-color: var(--color-border-accent); box-shadow: var(--shadow-card-hover);">
            <div class="ds-state-thumb gradient-thumb-2"></div>
            <div class="p-3">
              <p class="text-body-sm font-medium truncate">Interstellar</p>
              <p class="text-mono-sm text-text-muted">2014 · 2h 49m</p>
            </div>
          </div>
          <div class="text-center">
            <span class="text-label text-text-primary block">Hover</span>
            <span class="text-mono-sm text-text-disabled">Subtle lift, edge highlight.</span>
          </div>
        </div>

        <!-- Focus -->
        <div class="flex flex-col items-center gap-3">
          <div class="ds-state-card surface-card-sharp no-lift" style="border-color: var(--color-border-accent-strong); box-shadow: var(--shadow-focus-accent), var(--shadow-card);">
            <div class="ds-state-thumb gradient-thumb-2"></div>
            <div class="p-3">
              <p class="text-body-sm font-medium truncate">Interstellar</p>
              <p class="text-mono-sm text-text-muted">2014 · 2h 49m</p>
            </div>
          </div>
          <div class="text-center">
            <span class="text-label text-text-primary block">Focus</span>
            <span class="text-mono-sm text-text-disabled">Keyboard focus ring.</span>
          </div>
        </div>

        <!-- Selected -->
        <div class="flex flex-col items-center gap-3">
          <div class="ds-state-card surface-card-sharp active no-lift">
            <div class="ds-state-thumb gradient-thumb-2">
              <div class="absolute top-2 left-2 w-5 h-5 bg-accent-500 flex items-center justify-center rounded-xs">
                <span class="text-bg text-xs font-bold">✓</span>
              </div>
            </div>
            <div class="p-3">
              <p class="text-body-sm font-medium text-text-accent truncate">Interstellar</p>
              <p class="text-mono-sm text-text-muted">2014 · 2h 49m</p>
            </div>
          </div>
          <div class="text-center">
            <span class="text-label text-text-accent block">Selected</span>
            <span class="text-mono-sm text-text-disabled">Signal glow & persistent state.</span>
          </div>
        </div>

        <!-- Disabled -->
        <div class="flex flex-col items-center gap-3">
          <div class="ds-state-card surface-card-sharp no-lift" style="opacity: 0.4; pointer-events: none;">
            <div class="ds-state-thumb gradient-thumb-2"></div>
            <div class="p-3">
              <p class="text-body-sm font-medium truncate">Interstellar</p>
              <p class="text-mono-sm text-text-muted">2014 · 2h 49m</p>
            </div>
          </div>
          <div class="text-center">
            <span class="text-label text-text-primary block">Disabled</span>
            <span class="text-mono-sm text-text-disabled">Lower contrast, muted.</span>
          </div>
        </div>
      </div>
    </section>

    <!-- ═══════════════════════════════ STATUS LED ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">STATUS LED</h2>
      </div>

      <Panel>
        <div class="p-5 flex flex-wrap items-center gap-6">
          {#each ledStatuses as status}
            <div class="flex items-center gap-2 text-label">
              <StatusLed {status} />
              {status}
            </div>
          {/each}
          <div class="flex items-center gap-2 text-label">
            <StatusLed status="active" pulse />
            active · pulse
          </div>
          <div class="flex items-center gap-2 text-label">
            <StatusLed status="accent" size="lg" pulse />
            accent · lg · pulse
          </div>
        </div>
      </Panel>
    </section>

    <!-- ═══════════════════════════════ GLOW & BORDERS ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">GLOW &amp; BORDERS</h2>
      </div>

      <div class="grid lg:grid-cols-2 gap-8">
        <div class="space-y-3">
          <h3 class="text-kicker">GLOW EFFECTS</h3>
          <Panel>
            <div class="p-6 flex flex-wrap items-center gap-8">
              <div class="flex flex-col items-center gap-3">
                <div class="w-20 h-20 bg-surface-2 border border-border-accent rounded-sm" style="box-shadow: var(--shadow-glow-accent)"></div>
                <span class="text-mono-sm text-text-muted">glow-subtle</span>
              </div>
              <div class="flex flex-col items-center gap-3">
                <div class="w-20 h-20 bg-surface-2 border border-border-accent rounded-sm" style="box-shadow: var(--shadow-glow-accent-strong)"></div>
                <span class="text-mono-sm text-text-muted">glow-strong</span>
              </div>
              <div class="flex flex-col items-center gap-3">
                <div class="w-20 h-20 bg-surface-2 glow-pulse rounded-sm"></div>
                <span class="text-mono-sm text-text-muted">glow-pulse</span>
              </div>
              <div class="flex flex-col items-center gap-3">
                <div class="w-20 h-20 bg-surface-2 border border-border-subtle rounded-sm" style="box-shadow: var(--shadow-glow-phosphor)"></div>
                <span class="text-mono-sm text-text-muted">glow-phosphor</span>
              </div>
            </div>
          </Panel>
        </div>

        <div class="space-y-3">
          <h3 class="text-kicker">BORDERS</h3>
          <Panel>
            <div class="p-6 flex flex-wrap gap-4">
              <div class="w-24 h-16 bg-surface-2 flex items-center justify-center border border-border-subtle rounded-sm">
                <span class="text-mono-sm text-text-muted">subtle</span>
              </div>
              <div class="w-24 h-16 bg-surface-2 flex items-center justify-center border border-border-default rounded-sm">
                <span class="text-mono-sm text-text-muted">default</span>
              </div>
              <div class="w-24 h-16 bg-surface-2 flex items-center justify-center border border-border-accent rounded-sm">
                <span class="text-mono-sm text-text-muted">accent</span>
              </div>
              <div class="w-24 h-16 bg-surface-2 flex items-center justify-center rounded-sm" style="border: 1px solid var(--color-border-accent-strong)">
                <span class="text-mono-sm text-text-accent">strong</span>
              </div>
              <div class="w-24 h-16 bg-surface-2 flex items-center justify-center rounded-sm" style="border: 1px solid var(--color-border-accent-strong); box-shadow: var(--shadow-glow-accent)">
                <span class="text-mono-sm text-text-accent">glow</span>
              </div>
            </div>
          </Panel>
        </div>
      </div>
    </section>

    <!-- ═══════════════════════════════ ELEVATION & DEPTH ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">ELEVATION &amp; DEPTH</h2>
      </div>

      <div class="grid lg:grid-cols-2 gap-8">
        <!-- Panels -->
        <div class="space-y-3">
          <h3 class="text-kicker">PANEL VARIANTS</h3>
          <div class="space-y-3">
            <Panel variant="panel">
              <div class="p-4 text-body">panel — machined bevel</div>
            </Panel>
            <Panel variant="well">
              <div class="p-4 text-body">well — recessed inset</div>
            </Panel>
            <Panel variant="elevated">
              <div class="p-4 text-body">elevated — floating glass</div>
            </Panel>
          </div>
        </div>

        <!-- Stacking diagram -->
        <div class="space-y-3">
          <h3 class="text-kicker">LAYER STACK</h3>
          <div class="ds-depth-stack">
            <div class="ds-depth-layer" style="background: var(--color-surface-3); border: 1px solid var(--color-border-accent); box-shadow: var(--shadow-glow-accent); z-index: 4; transform: translateY(-48px);">
              <span class="text-mono-sm text-text-accent">Signal Glass (Layer 3)</span>
            </div>
            <div class="ds-depth-layer" style="background: var(--color-surface-2); border: 1px solid var(--color-border-default); box-shadow: var(--shadow-card); z-index: 3; transform: translateY(-32px);">
              <span class="text-mono-sm text-text-secondary">Glass Card (Layer 2)</span>
            </div>
            <div class="ds-depth-layer" style="background: var(--color-surface-1); border: 1px solid var(--color-border-subtle); box-shadow: var(--shadow-panel); z-index: 2; transform: translateY(-16px);">
              <span class="text-mono-sm text-text-muted">Material Panel (Layer 1)</span>
            </div>
            <div class="ds-depth-layer" style="background: var(--color-bg); z-index: 1;">
              <span class="text-mono-sm text-text-disabled">Canvas (Layer 0)</span>
            </div>
          </div>
        </div>
      </div>
    </section>

    <!-- ═══════════════════════════════ LOADING ANIMATIONS ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">LOADING ANIMATIONS</h2>
      </div>

      <Panel>
        <div class="p-6 flex flex-wrap items-start gap-12">
          <div class="flex flex-col items-center gap-3">
            <div class="relative flex items-center justify-center w-20 h-20">
              <div class="route-loader-core-field"></div>
              <div class="route-loader-ripples">
                <div class="route-loader-ripple-ring"></div>
                <div class="route-loader-ripple-ring"></div>
                <div class="route-loader-ripple-ring"></div>
              </div>
              <LogoMark size={32} alt="" />
            </div>
            <span class="text-mono-sm text-text-muted">route loader</span>
          </div>
          <div class="flex flex-col items-center gap-3">
            <div class="w-20 h-20 flex items-center justify-center">
              <div class="spinner-inline w-8 h-8">
                <div class="spinner-inline-outer"></div>
                <div class="spinner-inline-inner"></div>
                <div class="spinner-inline-core"></div>
              </div>
            </div>
            <span class="text-mono-sm text-text-muted">inline spinner</span>
          </div>
          <div class="flex flex-col items-center gap-3">
            <div class="w-20 h-20 flex items-center justify-end gap-[3px] pb-4 pl-3">
              {#each [0, 0.15, 0.3, 0.45] as delay}
                <div
                  class="w-[3px] h-5 bg-accent-500/70"
                  style="animation: bar-bounce 0.85s {delay}s ease-in-out infinite"
                ></div>
              {/each}
            </div>
            <span class="text-mono-sm text-text-muted">bar bounce</span>
          </div>
          <div class="flex flex-col items-center gap-3">
            <div class="w-20 h-20 shimmer-demo"></div>
            <span class="text-mono-sm text-text-muted">shimmer</span>
          </div>
        </div>
      </Panel>
    </section>

    <!-- ═══════════════════════════════ METERS ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">METERS</h2>
      </div>

      <Panel>
        <div class="p-5 grid md:grid-cols-2 gap-6 max-w-3xl">
          <Meter value={32} label="Storage" showValue />
          <Meter value={78} label="CPU" showValue variant="phosphor" />
          <Meter value={12} label="Low" showValue />
          <Meter value={96} label="High" showValue variant="phosphor" />
        </div>
      </Panel>
    </section>

    <!-- ═══════════════════════════════ MOTION PRINCIPLES ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">MOTION PRINCIPLES</h2>
      </div>

      <div class="grid grid-cols-2 md:grid-cols-4 gap-4">
        {#each [
          { icon: "〜", name: "Subtle", desc: "Small lifts, soft fades." },
          { icon: "→", name: "Purposeful", desc: "Motion clarifies focus, progress, & state." },
          { icon: "⚙", name: "Mechanical Ease", desc: "Smooth, precise, no bouncy curves." },
          { icon: "♿", name: "Accessible", desc: "Respects reduced motion settings." },
        ] as principle}
          <Panel>
            <div class="p-5 text-center space-y-2">
              <div class="text-3xl text-text-accent">{principle.icon}</div>
              <p class="font-heading text-sm font-semibold">{principle.name}</p>
              <p class="text-mono-sm text-text-muted">{principle.desc}</p>
            </div>
          </Panel>
        {/each}
      </div>
    </section>

    <!-- ═══════════════════════════════ TIMINGS & EASING ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">TIMINGS &amp; EASING</h2>
      </div>

      <div class="grid lg:grid-cols-2 gap-8">
        <!-- Durations -->
        <div class="space-y-3">
          <h3 class="text-kicker">DURATIONS</h3>
          <Panel>
            <div class="p-5 space-y-4">
              {#each durations as dur}
                <div class="flex items-center gap-4">
                  <span class="text-label w-20">{dur.name}</span>
                  <div class="flex-1 h-2 bg-surface-1 relative overflow-hidden">
                    <div
                      class="ds-duration-bar"
                      style="width: {parseInt(dur.value) / 4}%; animation-duration: {dur.value}"
                    ></div>
                  </div>
                  <span class="text-mono-sm text-text-accent w-16 text-right">{dur.value}</span>
                </div>
              {/each}
            </div>
          </Panel>
        </div>

        <!-- Easing curves -->
        <div class="space-y-3">
          <h3 class="text-kicker">EASING CURVES</h3>
          <Panel>
            <div class="p-5 space-y-4">
              {#each easingCurves as curve}
                <div class="flex items-center gap-4">
                  <span class="text-label w-24">{curve.name}</span>
                  <div class="flex-1">
                    <span class="text-mono-sm text-text-muted">{curve.value}</span>
                  </div>
                </div>
              {/each}
            </div>
          </Panel>
        </div>
      </div>
    </section>

    <!-- ═══════════════════════════════ LOGO MARK ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">BRAND MARK</h2>
      </div>

      <Panel>
        <div class="p-6 flex items-end gap-8">
          {#each [24, 48, 72, 96] as size}
            <div class="flex flex-col items-center gap-2">
              <LogoMark {size} />
              <span class="text-mono-sm text-text-muted">{size}px</span>
            </div>
          {/each}
        </div>
      </Panel>
    </section>

    <!-- ═══════════════════════════════ DESIGN PRINCIPLES ═══════════════════════════════ -->
    <section>
      <div class="ds-section-header">
        <h2 class="ds-section-title">DESIGN PRINCIPLES</h2>
      </div>

      <div class="grid md:grid-cols-2 gap-4">
        <Panel>
          <div class="p-5 space-y-2">
            <h3 class="text-h3 text-text-accent">Consistent radii</h3>
            <p class="text-body text-text-secondary">
              Tight, controlled radii from a unified scale — <code class="text-mono text-text-accent">XS 4px</code> through
              <code class="text-mono text-text-accent">2XL 24px</code>. Subtle softening, never bubbly.
            </p>
          </div>
        </Panel>
        <Panel>
          <div class="p-5 space-y-2">
            <h3 class="text-h3 text-text-accent">Glow expresses state</h3>
            <p class="text-body text-text-secondary">
              Selection, focus, and activity use <code class="text-mono text-text-accent">glow-pulse</code> or
              full glow <code class="text-mono text-text-accent">box-shadow</code>. No static color-only state changes.
            </p>
          </div>
        </Panel>
        <Panel>
          <div class="p-5 space-y-2">
            <h3 class="text-h3 text-text-accent">Material base + glass overlay</h3>
            <p class="text-body text-text-secondary">
              Solid dark surfaces as the ground layer; glass for floating and interactive elements.
              Three escalating weights: glass-1 → glass-2 → glass-3.
            </p>
          </div>
        </Panel>
        <Panel>
          <div class="p-5 space-y-2">
            <h3 class="text-h3 text-text-accent">Brass accent only on active</h3>
            <p class="text-body text-text-secondary">
              <span class="text-text-accent">#f2c26a</span> is reserved for active/selected states.
              Always expressed with glow, never flat. Accent gradients for fills.
            </p>
          </div>
        </Panel>
      </div>
    </section>
  </div>
</main>

<style>
  /* ── Hero ── */
  .ds-hero {
    position: relative;
    padding: 3rem 1.5rem 0;
    background: linear-gradient(
      180deg,
      rgba(26, 20, 8, 0.3) 0%,
      var(--color-bg) 100%
    );
  }

  .ds-hero-inner {
    max-width: 1400px;
    margin: 0 auto;
    display: grid;
    grid-template-columns: auto 1fr;
    grid-template-rows: auto auto;
    gap: 2rem 3rem;
    align-items: start;
  }

  .ds-hero-brand {
    display: flex;
    align-items: center;
    gap: 1.25rem;
  }

  .ds-hero-title {
    font-family: var(--font-heading);
    font-size: 2.5rem;
    font-weight: 700;
    letter-spacing: 0.2em;
    line-height: 1;
    color: var(--color-text-primary);
  }

  .ds-hero-subtitle {
    font-family: var(--font-heading);
    font-size: 0.85rem;
    font-weight: 600;
    letter-spacing: 0.25em;
    color: var(--color-text-accent);
    margin-top: 0.25rem;
  }

  .ds-hero-version {
    font-family: var(--font-mono);
    font-size: 0.7rem;
    color: var(--color-text-muted);
    margin-top: 0.5rem;
  }

  .ds-hero-tagline {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
    justify-self: end;
    text-align: right;
  }

  .ds-hero-tagline-text {
    font-family: var(--font-heading);
    font-size: 1.1rem;
    font-weight: 600;
    color: var(--color-text-primary);
    letter-spacing: -0.01em;
  }

  .ds-hero-pillars {
    grid-column: 1 / -1;
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: 1rem;
    padding-top: 1.5rem;
    border-top: 1px solid var(--color-border-subtle);
  }

  .ds-pillar {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.35rem;
    text-align: center;
  }

  .ds-pillar-icon {
    font-size: 1.5rem;
    opacity: 0.8;
  }

  .ds-pillar-label {
    font-family: var(--font-heading);
    font-size: 0.7rem;
    font-weight: 600;
    letter-spacing: 0.12em;
    color: var(--color-text-primary);
  }

  .ds-pillar-desc {
    font-size: 0.72rem;
    color: var(--color-text-muted);
    line-height: 1.3;
  }

  .ds-hero-divider {
    margin-top: 2rem;
    height: 1px;
    background: linear-gradient(
      90deg,
      transparent 0%,
      var(--color-accent-800) 20%,
      var(--color-accent-500) 50%,
      var(--color-accent-800) 80%,
      transparent 100%
    );
    opacity: 0.5;
  }

  /* ── Section Headers ── */
  .ds-section-header {
    position: relative;
    padding: 0.75rem 1rem;
    margin-bottom: 1.5rem;
    border: 1px solid var(--color-border-subtle);
    border-left: 2px solid var(--color-accent-600);
    background: linear-gradient(
      90deg,
      rgba(242, 194, 106, 0.04) 0%,
      transparent 40%
    );
  }

  .ds-section-header::before,
  .ds-section-header::after {
    content: "";
    position: absolute;
    width: 12px;
    height: 12px;
    border-color: var(--color-accent-600);
    opacity: 0.6;
  }

  .ds-section-header::before {
    top: -1px;
    right: -1px;
    border-top: 2px solid;
    border-right: 2px solid;
  }

  .ds-section-header::after {
    bottom: -1px;
    right: -1px;
    border-bottom: 2px solid;
    border-right: 2px solid;
  }

  .ds-section-title {
    font-family: var(--font-heading);
    font-size: 0.8rem;
    font-weight: 600;
    letter-spacing: 0.15em;
    color: var(--color-text-primary);
  }

  .ds-section-desc {
    font-size: 0.8rem;
    color: var(--color-text-muted);
    margin-top: 0.25rem;
  }

  /* ── Color Swatches ── */
  .ds-swatch {
    width: 48px;
    height: 48px;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-sm);
  }

  .ds-swatch-signal {
    display: flex;
    align-items: center;
    justify-content: center;
  }

  /* ── Type Samples ── */
  .ds-type-sample {
    display: flex;
    flex-direction: column;
    gap: 0;
  }

  .ds-type-label {
    font-family: var(--font-mono);
    font-size: 0.65rem;
    color: var(--color-text-disabled);
    letter-spacing: 0.06em;
    text-transform: uppercase;
  }

  .ds-type-role {
    font-size: 0.72rem;
    color: var(--color-text-accent);
    font-weight: 500;
  }

  /* ── Surface Cards ── */
  .ds-surface-card {
    padding: 1.25rem;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-md);
    min-height: 180px;
    display: flex;
    flex-direction: column;
  }

  .ds-surface-signal {
    border-color: rgba(242, 194, 106, 0.25);
    box-shadow: 0 0 25px rgba(242, 194, 106, 0.06);
  }

  .ds-surface-card-header {
    display: flex;
    flex-direction: column;
    gap: 0.15rem;
  }

  .ds-surface-card-props {
    margin-top: auto;
    padding-top: 0.75rem;
    border-top: 1px solid var(--color-border-subtle);
    display: flex;
    flex-direction: column;
    gap: 0.35rem;
  }

  /* ── Interaction State Cards ── */
  .ds-state-card {
    width: 100%;
    max-width: 180px;
    overflow: hidden;
  }

  .ds-state-thumb {
    position: relative;
    width: 100%;
    aspect-ratio: 2 / 3;
  }

  /* ── Depth Stack ── */
  .ds-depth-stack {
    position: relative;
    display: flex;
    flex-direction: column;
    align-items: center;
    padding: 4rem 2rem 1rem;
    min-height: 280px;
  }

  .ds-depth-layer {
    width: 80%;
    padding: 1rem;
    display: flex;
    justify-content: flex-end;
    position: relative;
    border-radius: var(--radius-sm);
  }

  /* ── Duration bar ── */
  .ds-duration-bar {
    height: 100%;
    border-radius: var(--radius-full);
    background: linear-gradient(
      90deg,
      var(--color-accent-700),
      var(--color-accent-500)
    );
    box-shadow: 0 0 6px rgba(242, 194, 106, 0.3);
    animation: ds-bar-grow 2s ease-in-out infinite alternate;
  }

  @keyframes ds-bar-grow {
    0% { width: 20%; }
    100% { width: 100%; }
  }

  /* ── Shimmer ── */
  .shimmer-demo {
    background: linear-gradient(
      90deg,
      var(--color-surface-2) 0%,
      var(--color-surface-3) 40%,
      var(--color-surface-2) 80%
    );
    background-size: 200% 100%;
    animation: shimmer 1.15s linear infinite;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-sm);
  }

  @keyframes shimmer {
    0% { background-position: 200% 0; }
    100% { background-position: -200% 0; }
  }

  /* ── Responsive ── */
  @media (max-width: 768px) {
    .ds-hero-inner {
      grid-template-columns: 1fr;
      gap: 1.5rem;
    }

    .ds-hero-tagline {
      justify-self: start;
      text-align: left;
    }

    .ds-hero-pillars {
      grid-template-columns: repeat(2, 1fr);
    }

    .ds-hero-title {
      font-size: 1.75rem;
    }
  }
</style>
