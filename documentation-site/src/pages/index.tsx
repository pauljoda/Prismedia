import type {CSSProperties, ReactNode} from 'react';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import useBaseUrl from '@docusaurus/useBaseUrl';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import styles from './index.module.css';

const CAPABILITIES = [
  'Movies',
  'Series',
  'Videos',
  'Comics',
  'Manga',
  'eBooks',
  'Galleries',
  'Images',
  'Audio',
  'People',
  'Studios',
  'Tags',
  'Collections',
  'Plugins',
  'Files',
];

const FEATURES = [
  {
    kicker: 'Files',
    title: 'Manage the source library',
    body:
      'Browse watched roots, inspect linked entities, upload, create folders, rename, move, rescan, exclude, and delete from one focused file manager.',
  },
  {
    kicker: 'Streaming',
    title: 'On-demand HLS',
    body:
      'Videos transcode to HLS via ffmpeg as they are needed. Cached renditions are served directly from the app — no separate media server, no manual format conversion.',
  },
  {
    kicker: 'Audio',
    title: 'Your music collection, organized',
    body:
      'Albums, tracks, cover art, waveforms, people and studio linking. The same metadata pipeline as every other media type, with shuffle and a built-in player.',
  },
  {
    kicker: 'Metadata',
    title: 'Reviewable identification',
    body:
      'A durable Identify queue lets you run providers, compare proposals, choose artwork, walk child records, and accept only what belongs in your library.',
  },
  {
    kicker: 'Jobs',
    title: 'Background jobs you can see',
    body:
      'Scan, probe, thumbnail, sprite, waveform, HLS, subtitle, and import jobs run in the background. The Jobs dashboard mirrors every queue in real time — so you always know what the system is doing.',
  },
  {
    kicker: 'Reading',
    title: 'Comics, EPUBs, and PDFs',
    body:
      'Comic archives, EPUBs, and PDFs are first-class entities with a built-in reader — paged and webtoon comics, reflowable EPUBs, and a full PDF reader with search, zoom, and resume.',
  },
  {
    kicker: 'Jellyfin',
    title: 'Play in Infuse and Manet',
    body:
      'An experimental Jellyfin-compatible API lets clients like Infuse and Manet sign in and stream your video and audio, with per-profile NSFW filtering and two-way resume sync.',
  },
  {
    kicker: 'Library',
    title: 'Collections are simple groupings',
    body:
      'Create manual, dynamic, or hybrid collections as organizational views over movies, series, galleries, images, books, and audio tracks.',
  },
  {
    kicker: 'Deploy',
    title: 'One Docker image',
    body:
      'PostgreSQL, ffmpeg, the web server, and the worker ship as a single image. Mount /data and /media, expose port 8008, and you are running. Nothing else required.',
  },
];

const SHOWCASE = [
  {
    title: 'A dashboard built like an instrument panel.',
    body:
      'Every media type at a glance — recent activity, library totals, scan state, and job status. Dense, dark, and purposeful.',
    image: '/img/screenshots/dashboard.png',
    alt: 'Prismedia dashboard',
  },
  {
    title: 'Rich video playback, start to finish.',
    body:
      'HLS adaptive streaming, trickplay frame strip, multi-language subtitles with a dockable transcript panel, and inline metadata editing — all in one page.',
    image: '/img/screenshots/video-detail.png',
    alt: 'Video detail page with player and transcript',
  },
  {
    title: 'Files and catalog views stay connected.',
    body:
      'Move between watched folders, linked entities, scan exclusions, and catalog metadata without leaving the app.',
    image: '/img/screenshots/files.png',
    alt: 'Files workspace',
  },
  {
    title: 'Mobile is first-class, not a fallback.',
    body:
      'Browse, search, read, and play from any phone on your network. Every view is designed for touch before it scales up to desktop.',
    image: '/img/screenshots/mobile-video-detail.png',
    alt: 'Prismedia on mobile',
    portrait: true,
  },
];

function Hero() {
  const dashboardUrl = useBaseUrl('/img/screenshots/dashboard.png');

  return (
    <header className={styles.hero}>
      <div className={styles.heroGrid} aria-hidden />
      <div className={styles.heroVignette} aria-hidden />
      <div className={`container ${styles.heroInner}`}>
        <div className={styles.heroCopy}>
          <div className={styles.heroBrand}>
            <img
              src={useBaseUrl('/img/logo.png')}
              alt=""
              className={styles.heroLogo}
              width={96}
              height={96}
            />
            <span className={styles.heroWordmark}>Prismedia</span>
          </div>
          <Heading as="h1" className={styles.heroTitle}>
            A private home for your{' '}
            <span className={styles.heroAccent}>entire</span> collection.
          </Heading>
          <p className={styles.heroSubtitle}>
            Videos, comics, books, audio, galleries, and files — organized,
            searchable, and playable from any device on your network. One Docker
            image. No cloud. No configuration.
          </p>
          <div className={styles.actions}>
            <Link className={styles.primaryAction} to="/docs/getting-started/install">
              Get started
              <span className={styles.actionArrow} aria-hidden>→</span>
            </Link>
            <Link className={styles.secondaryAction} to="/docs/intro">
              Learn more
            </Link>
          </div>
          <dl className={styles.metaRow}>
            <div>
              <dt>Media types</dt>
              <dd>Video · Images · Books · Audio · Files</dd>
            </div>
            <div>
              <dt>Footprint</dt>
              <dd>One container · port 8008</dd>
            </div>
            <div>
              <dt>License</dt>
              <dd>Open source</dd>
            </div>
          </dl>
        </div>
        <div className={styles.heroVisual}>
          <div className={styles.heroFrame}>
            <div className={styles.windowDots} aria-hidden>
              <span /><span /><span />
            </div>
            <img src={dashboardUrl} alt="Prismedia dashboard" loading="eager" />
          </div>
        </div>
      </div>
    </header>
  );
}

function CapabilityStrip() {
  return (
    <section className={styles.strip}>
      <div className={`container ${styles.stripInner}`}>
        <p className={styles.stripLabel}>Manages</p>
        <ul className={styles.stripList}>
          {CAPABILITIES.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      </div>
    </section>
  );
}

function Pathways() {
  return (
    <section className={styles.pathways}>
      <div className="container">
        <div className={styles.sectionHeader}>
          <p className={styles.kicker}>Documentation</p>
          <Heading as="h2" className={styles.sectionTitle}>
            Where to start
          </Heading>
          <p className={styles.sectionLead}>
            Pick the section that matches what you are trying to do.
          </p>
        </div>
        <div className={styles.pathGrid}>
          <Link className={styles.pathItem} to="/docs/getting-started/install">
            <span className={styles.pathKicker}>01 · Run it</span>
            <strong className={styles.pathTitle}>Set up your media library</strong>
            <p className={styles.pathBody}>
              Install with Docker, mount your media directories, run a scan, and
              start browsing. Library organization, settings, and operations live here.
            </p>
            <span className={styles.pathCta}>
              Quick start
              <em aria-hidden>→</em>
            </span>
          </Link>
          <Link className={styles.pathItem} to="/docs/plugins/overview">
            <span className={styles.pathKicker}>02 · Extend it</span>
            <strong className={styles.pathTitle}>Build a metadata plugin</strong>
            <p className={styles.pathBody}>
              Write providers in TypeScript or Python to identify videos, books,
              people, audio, and more. Stash-compatible scraper packages can be adapted.
            </p>
            <span className={styles.pathCta}>
              Build plugins
              <em aria-hidden>→</em>
            </span>
          </Link>
          <Link className={styles.pathItem} to="/docs/developers/architecture">
            <span className={styles.pathKicker}>03 · Understand it</span>
            <strong className={styles.pathTitle}>Explore the architecture</strong>
            <p className={styles.pathBody}>
              Svelte, the .NET worker, Postgres, and the shared packages. How code
              moves from the UI all the way to the database and job queue.
            </p>
            <span className={styles.pathCta}>
              Architecture
              <em aria-hidden>→</em>
            </span>
          </Link>
        </div>
      </div>
    </section>
  );
}

function Features() {
  return (
    <section className={styles.features}>
      <div className="container">
        <div className={styles.sectionHeader}>
          <p className={styles.kicker}>What it does</p>
          <Heading as="h2" className={styles.sectionTitle}>
            What Prismedia manages
          </Heading>
          <p className={styles.sectionLead}>
            Built for a single trusted user on a private network. All processing happens
            locally — no internet access required, no external services.
          </p>
        </div>
        <div className={styles.featureGrid}>
          {FEATURES.map((f) => (
            <article key={f.title} className={styles.featureCard}>
              <p className={styles.featureKicker}>{f.kicker}</p>
              <h3 className={styles.featureTitle}>{f.title}</h3>
              <p className={styles.featureBody}>{f.body}</p>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}

function Showcase() {
  return (
    <section className={styles.showcase}>
      <div className="container">
        <div className={styles.sectionHeader}>
          <p className={styles.kicker}>The interface</p>
          <Heading as="h2" className={styles.sectionTitle}>
            One light, every medium: neutral chrome, entity spectrum, and glass only when it floats.
          </Heading>
          <p className={styles.sectionLead}>
            The whole UI follows one design language. Read the{' '}
            <Link to="/docs/developers/design-language">Design Language</Link> doc for
            the full spec.
          </p>
        </div>
        <div className={styles.showcaseList}>
          {SHOWCASE.map((item, i) => (
            <article
              key={item.image}
              className={`${styles.showcaseRow} ${
                i % 2 === 1 ? styles.showcaseRowReverse : ''
              }`}
            >
              <div className={styles.showcaseCopy}>
                <h3 className={styles.showcaseTitle}>{item.title}</h3>
                <p className={styles.showcaseBody}>{item.body}</p>
              </div>
              <div
                className={`${styles.showcaseFrame} ${
                  item.portrait ? styles.showcaseFramePortrait : ''
                }`}
              >
                <div className={styles.windowDots} aria-hidden>
                  <span /><span /><span />
                </div>
                <img src={useBaseUrl(item.image)} alt={item.alt} loading="lazy" />
              </div>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}

function SectionDivider() {
  return (
    <div className={styles.sectionDivider} aria-hidden>
      <span className={styles.sectionDividerDot} />
    </div>
  );
}

function CtaBlock() {
  return (
    <section className={styles.cta}>
      <div className={`container ${styles.ctaInner}`}>
        <Heading as="h2" className={styles.ctaTitle}>
          Run it on your own hardware
        </Heading>
        <p className={styles.ctaSubtext}>
          Mount your media, expose one port, and you are running.
          No cloud accounts and no external dependencies.
        </p>
        <div className={styles.ctaCode}>
          <kbd>docker pull ghcr.io/pauljoda/prismedia:latest</kbd>
        </div>
        <div className={styles.ctaActions}>
          <Link className={styles.primaryAction} to="/docs/getting-started/install">
            Get started
            <span className={styles.actionArrow} aria-hidden>→</span>
          </Link>
          <Link
            className={styles.secondaryAction}
            href="https://github.com/pauljoda/Prismedia"
          >
            View on GitHub
          </Link>
          <Link
            className={styles.secondaryAction}
            href="https://www.reddit.com/r/Prismedia/"
          >
            Join the community
          </Link>
        </div>
      </div>
    </section>
  );
}

export default function Home(): ReactNode {
  const {siteConfig} = useDocusaurusContext();
  return (
    <Layout
      title={siteConfig.title}
      description="Prismedia is a private, self-hosted media library for videos, comics, books, audio, and galleries. One Docker image. No cloud. No configuration."
    >
      <Hero />
      <main>
        <CapabilityStrip />
        <Pathways />
        <SectionDivider />
        <Features />
        <SectionDivider />
        <Showcase />
        <SectionDivider />
        <CtaBlock />
      </main>
    </Layout>
  );
}
