<script lang="ts">
  import type { Snippet } from "svelte";
  import { flyUp } from "@prismedia/ui-svelte";

  interface Props {
    title?: string;
    subtitle?: string;
    /** Wider panel for multi-column steps (setup review). */
    wide?: boolean;
    children: Snippet;
  }

  let { title, subtitle, wide = false, children }: Props = $props();
</script>

<!--
  Full-screen entrance chrome shared by /login and /setup: dark ground, neutral accent radial
  glow, and a single glass panel. Uses the plain logo image (never the NSFW-aware
  LogoMark) because nothing NSFW may render before authentication.
-->
<div class="auth-shell relative flex min-h-dvh items-center justify-center overflow-hidden px-4 py-10">
  <div class="auth-glow" aria-hidden="true"></div>

  <div
    class={["auth-panel relative z-10 w-full rounded-2xl p-8", wide ? "max-w-xl" : "max-w-md"]}
    transition:flyUp={{ duration: 300 }}
  >
    <div class="mb-7 flex flex-col items-center gap-3 text-center">
      <div class="auth-brand-mark">
        <img src="/brand/prismedia-logo.png" alt="" class="size-16" />
      </div>
      <h1 class="font-display text-2xl tracking-[0.3em] text-text-primary uppercase">Prismedia</h1>
      <div class="auth-divider" aria-hidden="true"></div>
      {#if title}
        <p class="text-sm text-text-secondary">{title}</p>
      {/if}
      {#if subtitle}
        <p class="max-w-sm font-mono text-xs text-text-disabled">{subtitle}</p>
      {/if}
    </div>

    {@render children()}
  </div>
</div>

<style>
  .auth-shell {
    background:
      radial-gradient(ellipse 90% 70% at 50% 115%, rgb(13 16 24 / 0.9), transparent 60%),
      var(--color-bg, #07080b);
  }

  .auth-glow {
    position: absolute;
    inset: 0;
    background:
      radial-gradient(ellipse 60% 45% at 50% 36%, rgb(244 204 134 / 0.11), transparent 70%),
      radial-gradient(ellipse 90% 55% at 50% 112%, rgb(199 201 204 / 0.07), transparent 70%);
    pointer-events: none;
  }

  /* Material base + glass overlay per the design language: a solid-leaning glass plate
     with a vertical sheen, a neutral accent hairline along the top edge, and a faint outer bloom
     so the panel reads like the dashboard hero rather than a bare form. */
  .auth-panel {
    background:
      linear-gradient(180deg, rgb(255 255 255 / 0.035), transparent 30%),
      rgb(17 21 33 / 0.92);
    backdrop-filter: blur(24px);
    -webkit-backdrop-filter: blur(24px);
    border: 1px solid rgb(255 255 255 / 0.08);
    border-top-color: rgb(199 201 204 / 0.28);
    box-shadow:
      inset 0 1px 0 rgb(244 204 134 / 0.1),
      0 24px 80px rgb(0 0 0 / 0.6),
      0 0 90px rgb(199 201 204 / 0.05);
  }

  /* Hairline neutral accent divider between the brand moment and the step content. */
  .auth-divider {
    width: 7rem;
    height: 1px;
    margin-top: 0.15rem;
    background: linear-gradient(
      to right,
      transparent,
      rgb(199 201 204 / 0.45) 30%,
      rgb(199 201 204 / 0.45) 70%,
      transparent
    );
    box-shadow: 0 0 8px rgb(199 201 204 / 0.25);
  }

  .auth-brand-mark {
    position: relative;
    isolation: isolate;
  }

  .auth-brand-mark::before {
    content: "";
    position: absolute;
    inset: -0.5rem;
    z-index: -1;
    background:
      radial-gradient(circle at 50% 47%, rgb(244 204 134 / 0.24), transparent 44%),
      radial-gradient(circle at 50% 52%, rgb(199 201 204 / 0.2), transparent 72%);
    filter: blur(0.25rem);
  }

  .auth-brand-mark img {
    filter:
      drop-shadow(0 0 10px rgb(244 204 134 / 0.4)) drop-shadow(0 0 26px rgb(199 201 204 / 0.22));
  }
</style>
