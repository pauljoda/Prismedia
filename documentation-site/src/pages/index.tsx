import {useEffect, type CSSProperties, type ReactNode} from 'react';
import Link from '@docusaurus/Link';
import useBaseUrl from '@docusaurus/useBaseUrl';
import {useLocation} from '@docusaurus/router';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import styles from './index.module.css';

const TESTFLIGHT_URL = 'https://testflight.apple.com/join/c9bgDxr7';
const GITHUB_URL = 'https://github.com/pauljoda/Prismedia';
const PRODUCT_SCHEMA = {
  '@context': 'https://schema.org',
  '@type': 'SoftwareApplication',
  name: 'Prismedia',
  applicationCategory: 'MultimediaApplication',
  operatingSystem: 'Web, iOS, iPadOS, tvOS',
  description:
    'A private, self-hosted media library that connects discovery, acquisition, metadata, files, playback, reading, and listening.',
  url: 'https://pauljoda.github.io/Prismedia/',
  image:
    'https://pauljoda.github.io/Prismedia/img/showcase/prism-refraction-hero.png',
  softwareRequirements: 'Docker',
  sameAs: [GITHUB_URL, TESTFLIGHT_URL],
};
const SECTION_IDS = new Set([
  'product',
  'experiences',
  'platforms',
  'self-hosting',
]);

const LIFECYCLE = [
  {
    index: '01',
    name: 'Discover',
    detail: 'Search the providers you choose and decide what belongs.',
    color: 'var(--prismedia-material-cyan)',
  },
  {
    index: '02',
    name: 'Acquire',
    detail: 'Review releases, follow the transfer, and import with context.',
    color: 'var(--prismedia-material-green)',
  },
  {
    index: '03',
    name: 'Identify',
    detail: 'Compare proposals, artwork, and metadata before accepting.',
    color: 'var(--prismedia-material-yellow)',
  },
  {
    index: '04',
    name: 'Enjoy',
    detail: 'Watch, listen, read, and browse with personal progress.',
    color: 'var(--prismedia-material-red)',
  },
  {
    index: '05',
    name: 'Maintain',
    detail: 'Keep files, scans, jobs, users, and failures in view.',
    color: 'var(--prismedia-material-violet)',
  },
] as const;

const MEDIA_TYPES = [
  'Movies',
  'Series',
  'Videos',
  'Music',
  'Audiobooks',
  'Books',
  'Comics',
  'eBooks',
  'Images',
  'Galleries',
  'People',
  'Collections',
] as const;

type ImageProps = {
  src: string;
  alt: string;
  className?: string;
  loading?: 'eager' | 'lazy';
};

function ProductImage({
  src,
  alt,
  className = '',
  loading = 'lazy',
}: ImageProps) {
  return (
    <img
      src={useBaseUrl(src)}
      alt={alt}
      className={className}
      loading={loading}
      decoding="async"
    />
  );
}

function ArrowIcon() {
  return <span aria-hidden>↗</span>;
}

function SectionRoute() {
  const location = useLocation();
  const homeUrl = useBaseUrl('/');

  useEffect(() => {
    const section = new URLSearchParams(location.search).get('section');
    if (!section || !SECTION_IDS.has(section)) {
      return;
    }

    const frame = window.requestAnimationFrame(() => {
      document.getElementById(section)?.scrollIntoView();
      window.history.replaceState(
        window.history.state,
        '',
        `${homeUrl}#${section}`,
      );
    });

    return () => window.cancelAnimationFrame(frame);
  }, [homeUrl, location.search]);

  return null;
}

function TestFlightButton({compact = false}: {compact?: boolean}) {
  return (
    <Link
      className={`${styles.testFlightButton} ${
        compact ? styles.testFlightButtonCompact : ''
      }`}
      href={TESTFLIGHT_URL}
    >
      <ProductImage
        src="/img/testflight-icon.webp"
        alt=""
        className={styles.testFlightIcon}
      />
      <span>
        <small>Join the beta</small>
        View in TestFlight
      </span>
    </Link>
  );
}

function Frame({
  children,
  className = '',
  label,
}: {
  children: ReactNode;
  className?: string;
  label?: string;
}) {
  return (
    <figure className={`${styles.frame} ${className}`}>
      <div className={styles.frameBar} aria-hidden>
        <span />
        <span />
        <span />
        {label ? <em>{label}</em> : null}
      </div>
      {children}
    </figure>
  );
}

function Phone({
  src,
  alt,
  className = '',
}: {
  src: string;
  alt: string;
  className?: string;
}) {
  return (
    <figure className={`${styles.phone} ${className}`}>
      <span className={styles.phoneSpeaker} aria-hidden />
      <ProductImage src={src} alt={alt} />
    </figure>
  );
}

function Hero() {
  return (
    <header className={styles.hero}>
      <ProductImage
        src="/img/showcase/prism-refraction-hero.webp"
        alt=""
        className={styles.heroAtmosphere}
        loading="eager"
      />
      <div className={styles.heroScrim} aria-hidden />
      <div className={styles.heroGrid} aria-hidden />
      <div className={`container ${styles.heroInner}`}>
        <div className={styles.heroCopy}>
          <p className={styles.eyebrow}>Private · self-hosted · made for the household</p>
          <Heading as="h1" className={styles.heroTitle}>
            Your whole media life.
            <span>One private home.</span>
          </Heading>
          <p className={styles.heroLead}>
            Prismedia keeps discovery, requests, downloads, metadata, files,
            playback, and reading connected—across web, iPhone, iPad, and
            Apple TV.
          </p>
          <div className={styles.heroActions}>
            <Link
              className={styles.primaryAction}
              to="/docs/getting-started/install"
            >
              Install Prismedia <span aria-hidden>→</span>
            </Link>
            <TestFlightButton compact />
          </div>
          <Link className={styles.sourceLink} href={GITHUB_URL}>
            View the source <ArrowIcon />
          </Link>
          <ul className={styles.proofRail} aria-label="Prismedia at a glance">
            <li>One Docker image</li>
            <li>One exposed port</li>
            <li>Household accounts</li>
            <li>Source available</li>
          </ul>
        </div>

        <div className={styles.heroProduct}>
          <div className={styles.heroBeam} aria-hidden />
          <Frame label="Prismedia · Web" className={styles.heroFrame}>
            <ProductImage
              src="/img/showcase/web-dashboard.webp"
              alt="Prismedia web dashboard showing mixed media in Continue"
              loading="eager"
            />
          </Frame>
          <Phone
            src="/img/showcase/ios-dashboard.webp"
            alt="Prismedia native iPhone dashboard"
            className={styles.heroPhone}
          />
          <div className={styles.heroPlatformTag}>
            <span className={styles.liveDot} aria-hidden />
            Web · iPhone · iPad · Apple TV
          </div>
        </div>
      </div>
    </header>
  );
}

function MediaRail() {
  return (
    <section className={styles.mediaRail} aria-label="Supported media">
      <div className="container">
        <p className={styles.mediaRailLabel}>One library, purpose-built experiences</p>
        <ul>
          {MEDIA_TYPES.map((type) => (
            <li key={type}>{type}</li>
          ))}
        </ul>
      </div>
    </section>
  );
}

function Problem() {
  return (
    <section className={styles.problem} id="product">
      <div className={`container ${styles.problemGrid}`}>
        <div>
          <p className={styles.kicker}>The collection is already one thing</p>
          <Heading as="h2" className={styles.displayTitle}>
            Managing it should feel that way.
          </Heading>
        </div>
        <div className={styles.problemCopy}>
          <p>
            Finding something, bringing it home, fixing its metadata,
            organizing its files, and finally enjoying it often means crossing
            a chain of disconnected tools.
          </p>
          <p>
            Prismedia keeps the item and its history intact from the first
            request to the next play.
          </p>
        </div>
      </div>
      <div className={`container ${styles.lifecycleGrid}`}>
        {LIFECYCLE.map((step) => (
          <article
            key={step.name}
            className={styles.lifecycleStep}
            style={{'--step-color': step.color} as CSSProperties}
          >
            <span>{step.index}</span>
            <h3>{step.name}</h3>
            <p>{step.detail}</p>
          </article>
        ))}
      </div>
      <div className={`container ${styles.requestProof}`}>
        <Frame label="Request · The Movie Database">
          <ProductImage
            src="/img/showcase/web-request.webp"
            alt="Prismedia Request showing a movie search and provider candidates"
          />
        </Frame>
        <div className={styles.requestCaption}>
          <p className={styles.kicker}>One lifecycle</p>
          <Heading as="h3">Wanted today. Available tomorrow. Still the same item.</Heading>
          <p>
            Provider identity, acquisition state, files, artwork, history, and
            progress stay connected as an item moves through Prismedia.
          </p>
        </div>
      </div>
    </section>
  );
}

function VideoExperience() {
  return (
    <article className={`${styles.experience} ${styles.videoExperience}`}>
      <div className={`container ${styles.experienceGrid}`}>
        <div className={styles.experienceCopy}>
          <p className={styles.kicker}>Video · Movies · Series</p>
          <Heading as="h2" className={styles.displayTitle}>
            A theater, not a file list.
          </Heading>
          <p className={styles.experienceLead}>
            Direct play when the screen can handle it. Stream copy when it can.
            On-demand HLS when it cannot. The choice stays out of the way while
            subtitles, transcripts, trickplay, and resume stay close.
          </p>
          <ul className={styles.featureList}>
            <li>Direct Play, stream copy, and adaptive HLS</li>
            <li>Subtitles, docked transcripts, and trickplay</li>
            <li>Personal progress across the household</li>
            <li>Native playback built for the television</li>
          </ul>
        </div>
        <Frame label="Movie detail · artwork reactive">
          <ProductImage
            src="/img/showcase/web-detail.webp"
            alt="Prismedia movie detail page with artwork-reactive atmosphere"
          />
        </Frame>
      </div>
      <div className={`container ${styles.tvMoment}`}>
        <div className={styles.tvScreen}>
          <ProductImage
            src="/img/showcase/tvos-playback.webp"
            alt="A movie paused in Prismedia on Apple TV with playback controls visible"
          />
        </div>
        <div className={styles.tvCaption}>
          <span className={styles.platformLabel}>Apple TV</span>
          <Heading as="h3">Pause the movie. See the whole playback story.</Heading>
          <p>
            Title, direct-play state, resolution, codecs, timeline, audio,
            subtitles, and speed controls stay readable from the couch.
          </p>
        </div>
      </div>
    </article>
  );
}

function ReadingExperience() {
  return (
    <article className={`${styles.experience} ${styles.readingExperience}`}>
      <div className={`container ${styles.readingGrid}`}>
        <div className={styles.experienceCopy}>
          <p className={styles.kicker}>Books · Comics · eBooks · Audiobooks</p>
          <Heading as="h2" className={styles.displayTitle}>
            Read it your way. Or listen and read together.
          </Heading>
          <p className={styles.experienceLead}>
            EPUB, PDF, and comic reading are first-class experiences—not a
            download link. Tune the page, keep your place, then move between the
            written and narrated edition from one book.
          </p>
          <ul className={styles.featureList}>
            <li>EPUB, PDF, paged comics, and webtoon layouts</li>
            <li>Paper, white, sepia, soft gray, and dark themes</li>
            <li>Typeface, size, weight, line, letter, and word spacing</li>
            <li>Combined reading and audiobook progress</li>
          </ul>
        </div>
        <div className={styles.readerComposition}>
          <Phone
            src="/img/showcase/ios-reader.webp"
            alt="A Game of Thrones open in Prismedia's dark EPUB reader with Literary Serif"
            className={styles.readerMain}
          />
          <Phone
            src="/img/showcase/ios-reader-settings.webp"
            alt="Prismedia reader settings showing dark theme, Literary Serif, text size, weight, and spacing"
            className={styles.readerSettings}
          />
        </div>
      </div>
      <div className={`container ${styles.combinedReading}`}>
        <Phone
          src="/img/showcase/ios-book-combined.webp"
          alt="A Game of Thrones detail page showing combined reading and listening progress"
        />
        <div>
          <p className={styles.kicker}>The same book, in two forms</p>
          <Heading as="h3">Reading and listening share one place in your library.</Heading>
          <p>
            The book detail keeps reading and audiobook progress side by side,
            with separate Continue Reading and Continue Listening actions.
          </p>
        </div>
      </div>
    </article>
  );
}

function AudioExperience() {
  return (
    <article className={`${styles.experience} ${styles.audioExperience}`}>
      <div className={`container ${styles.audioGrid}`}>
        <div className={styles.audioPhones}>
          <Phone
            src="/img/showcase/ios-music-player.webp"
            alt="Prismedia native music player with album art and playback controls"
          />
          <Phone
            src="/img/showcase/ios-audiobook.webp"
            alt="Prismedia native audiobook player for A Game of Thrones"
          />
        </div>
        <div className={styles.experienceCopy}>
          <p className={styles.kicker}>Music · Albums · Tracks · Audiobooks</p>
          <Heading as="h2" className={styles.displayTitle}>
            A real music player. A focused audiobook experience.
          </Heading>
          <p className={styles.experienceLead}>
            Artwork shapes the atmosphere while queue, AirPlay, shuffle,
            repeat, chapter position, and transport controls stay native and
            familiar.
          </p>
          <div className={styles.audioProof}>
            <span>Albums</span>
            <span>Artists</span>
            <span>Tracks</span>
            <span>Queue</span>
            <span>AirPlay</span>
            <span>Read + listen</span>
          </div>
        </div>
      </div>
    </article>
  );
}

function ImageExperience() {
  return (
    <article className={`${styles.experience} ${styles.imageExperience}`}>
      <div className={`container ${styles.imageGrid}`}>
        <div className={styles.experienceCopy}>
          <p className={styles.kicker}>Images · Galleries · Collections</p>
          <Heading as="h2" className={styles.displayTitle}>
            The visual library gets room to breathe.
          </Heading>
          <p className={styles.experienceLead}>
            Browse individual images, move through galleries in a dedicated
            lightbox, and connect artwork to people, studios, tags, and
            collections without flattening everything into a poster grid.
          </p>
        </div>
        <Frame label="Galleries · Web">
          <ProductImage
            src="/img/screenshots/galleries.png"
            alt="Prismedia galleries interface"
          />
        </Frame>
      </div>
    </article>
  );
}

function Experiences() {
  return (
    <section className={styles.experiences} id="experiences">
      <div className={`container ${styles.experiencesIntro}`}>
        <p className={styles.kicker}>Every medium deserves an experience</p>
        <Heading as="h2" className={styles.displayTitle}>
          One foundation. Purpose-built ways to enjoy it.
        </Heading>
        <p>
          Shared identity, files, progress, and relationships underneath.
          Interfaces shaped around what you are actually doing on top.
        </p>
      </div>
      <VideoExperience />
      <ReadingExperience />
      <AudioExperience />
      <ImageExperience />
    </section>
  );
}

function Platforms() {
  return (
    <section className={styles.platforms} id="platforms">
      <div className={`container ${styles.platformHeader}`}>
        <div>
          <p className={styles.kicker}>Prismedia everywhere</p>
          <Heading as="h2" className={styles.displayTitle}>
            One product family. Each screen used properly.
          </Heading>
        </div>
        <p>
          The server, complete web workspace, native mobile experience, and
          living-room experience share the same library and household state.
        </p>
      </div>
      <div className={`container ${styles.platformGrid}`}>
        <article className={`${styles.platformCard} ${styles.platformWeb}`}>
          <div className={styles.platformCardCopy}>
            <span>01 · Web</span>
            <Heading as="h3">The complete library workspace.</Heading>
            <p>
              Browse every medium, request and identify items, manage files,
              tune settings, and watch background work from one responsive
              interface.
            </p>
          </div>
          <Frame>
            <ProductImage
              src="/img/showcase/web-movies.webp"
              alt="Prismedia movie library on the web"
            />
          </Frame>
        </article>

        <article className={`${styles.platformCard} ${styles.platformPhone}`}>
          <div className={styles.platformCardCopy}>
            <span>02 · iPhone + iPad</span>
            <Heading as="h3">Native where touch matters.</Heading>
            <p>
              Browse, continue, watch, listen, and read with adaptive
              navigation and Apple-platform controls.
            </p>
          </div>
          <Phone
            src="/img/showcase/ios-movies.webp"
            alt="Prismedia native movie library on iPhone"
          />
        </article>

        <article className={`${styles.platformCard} ${styles.platformTv}`}>
          <div className={styles.platformCardCopy}>
            <span>03 · Apple TV</span>
            <Heading as="h3">The collection comes home.</Heading>
            <p>
              A cinematic, focus-first interface for the biggest screen in the
              house.
            </p>
          </div>
          <div className={styles.platformTvScreen}>
            <ProductImage
              src="/img/showcase/tvos-dashboard.webp"
              alt="Prismedia home screen on Apple TV"
            />
          </div>
        </article>
      </div>
    </section>
  );
}

function SelfHosting() {
  return (
    <section className={styles.selfHosting} id="self-hosting">
      <div className={`container ${styles.selfHostingGrid}`}>
        <div className={styles.selfHostingCopy}>
          <p className={styles.kicker}>Your hardware</p>
          <Heading as="h2" className={styles.displayTitle}>
            One image in. A complete library boots.
          </Heading>
          <p>
            Prismedia packages PostgreSQL, ffmpeg, the web app, the .NET API,
            and the background worker into one Docker image. Mount your data
            and media, expose port 8008, and complete setup in the browser.
          </p>
          <div className={styles.selfHostingActions}>
            <Link
              className={styles.primaryAction}
              to="/docs/getting-started/install"
            >
              Read the install guide <span aria-hidden>→</span>
            </Link>
            <Link className={styles.secondaryAction} href={GITHUB_URL}>
              Explore the source <ArrowIcon />
            </Link>
          </div>
        </div>
        <div className={styles.topology} aria-label="Prismedia deployment model">
          <div className={styles.topologyInput}>
            <span>/media</span>
            <span>/data</span>
          </div>
          <div className={styles.topologyBeam} aria-hidden />
          <div className={styles.topologyCore}>
            <ProductImage src="/img/logo.png" alt="" />
            <strong>Prismedia</strong>
            <small>port 8008</small>
          </div>
          <div className={styles.topologySpectrum} aria-hidden>
            <i />
            <i />
            <i />
            <i />
          </div>
          <div className={styles.topologyDevices}>
            <span>Web</span>
            <span>iPhone</span>
            <span>iPad</span>
            <span>Apple TV</span>
          </div>
        </div>
      </div>
    </section>
  );
}

function FinalCta() {
  return (
    <section className={styles.finalCta}>
      <div className={`container ${styles.finalCtaInner}`}>
        <ProductImage src="/img/logo.png" alt="" className={styles.finalLogo} />
        <p className={styles.kicker}>Bring the whole collection into focus</p>
        <Heading as="h2" className={styles.displayTitle}>
          Self-host the library. Take the experience everywhere.
        </Heading>
        <div className={styles.finalActions}>
          <Link
            className={styles.primaryAction}
            to="/docs/getting-started/install"
          >
            Install Prismedia <span aria-hidden>→</span>
          </Link>
          <TestFlightButton />
        </div>
        <div className={styles.finalLinks}>
          <Link to="/docs/intro">Read the docs</Link>
          <Link href={GITHUB_URL}>GitHub</Link>
          <Link href="https://www.reddit.com/r/Prismedia/">Community</Link>
        </div>
      </div>
    </section>
  );
}

export default function Home(): ReactNode {
  return (
    <Layout
      title="Your whole media life. One private home."
      description="Prismedia is a private, self-hosted media library that connects discovery, acquisition, metadata, files, playback, reading, and listening across web, iPhone, iPad, and Apple TV."
    >
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{__html: JSON.stringify(PRODUCT_SCHEMA)}}
      />
      <SectionRoute />
      <Hero />
      <main>
        <MediaRail />
        <Problem />
        <Experiences />
        <Platforms />
        <SelfHosting />
        <FinalCta />
      </main>
    </Layout>
  );
}
