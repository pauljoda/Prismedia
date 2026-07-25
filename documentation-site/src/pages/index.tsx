import {
  useEffect,
  useRef,
  useState,
  type CSSProperties,
  type ReactNode,
} from 'react';
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
const VIDEO_SCHEMA = {
  '@context': 'https://schema.org',
  '@type': 'VideoObject',
  name: 'Prismedia — Your whole media life. One private home.',
  description:
    'A silent product film showing Prismedia requests, playback, reader customization, combined reading and listening, music, audiobooks, and native experiences across web, iPhone, iPad, and Apple TV.',
  thumbnailUrl:
    'https://pauljoda.github.io/Prismedia/img/showcase/prismedia-launch-poster.webp',
  contentUrl:
    'https://pauljoda.github.io/Prismedia/video/prismedia-launch.mp4',
  uploadDate: '2026-07-24',
  duration: 'PT1M12S',
  isFamilyFriendly: true,
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

const SPECTRUM_MEDIA = [
  {
    label: 'Movies',
    color: 'var(--prismedia-material-red)',
    angle: '-20deg',
    counterAngle: '20deg',
    length: '76%',
  },
  {
    label: 'Series',
    color: '#b9543f',
    angle: '-15deg',
    counterAngle: '15deg',
    length: '82%',
  },
  {
    label: 'Videos',
    color: 'var(--prismedia-material-orange)',
    angle: '-10deg',
    counterAngle: '10deg',
    length: '88%',
  },
  {
    label: 'Music',
    color: 'var(--prismedia-material-yellow)',
    angle: '-5deg',
    counterAngle: '5deg',
    length: '94%',
  },
  {
    label: 'Audiobooks',
    color: 'var(--prismedia-material-green)',
    angle: '0deg',
    counterAngle: '0deg',
    length: '98%',
  },
  {
    label: 'Books',
    color: 'var(--prismedia-material-cyan)',
    angle: '5deg',
    counterAngle: '-5deg',
    length: '94%',
  },
  {
    label: 'Comics',
    color: '#467eaa',
    angle: '10deg',
    counterAngle: '-10deg',
    length: '88%',
  },
  {
    label: 'Images',
    color: 'var(--prismedia-material-blue)',
    angle: '15deg',
    counterAngle: '-15deg',
    length: '82%',
  },
  {
    label: 'Galleries',
    color: 'var(--prismedia-material-violet)',
    angle: '20deg',
    counterAngle: '-20deg',
    length: '76%',
  },
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

function PrismFlow({
  compact = false,
  inputLabel = 'One private library',
}: {
  compact?: boolean;
  inputLabel?: string;
}) {
  return (
    <div
      className={`${styles.prismFlow} ${
        compact ? styles.prismFlowCompact : ''
      }`}
      aria-label={
        compact
          ? undefined
          : 'One private library enters Prismedia and becomes purpose-built experiences for every media type.'
      }
      aria-hidden={compact || undefined}
      role={compact ? undefined : 'img'}
    >
      <div className={styles.prismFlowInput}>
        <span>{inputLabel}</span>
        <i className={styles.prismFlowInputLine} />
      </div>
      <div className={styles.prismFlowMark}>
        <ProductImage src="/img/logo-mark.png" alt="" />
        <strong>prismedia</strong>
      </div>
      <ol className={styles.prismFlowOutputs}>
        {SPECTRUM_MEDIA.map((item) => (
          <li
            key={item.label}
            style={
              {
                '--ray-color': item.color,
                '--ray-angle': item.angle,
                '--ray-counter-angle': item.counterAngle,
                '--ray-length': item.length,
              } as CSSProperties
            }
          >
            <i />
            <span>{item.label}</span>
          </li>
        ))}
      </ol>
    </div>
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
            <br />
            <span className={styles.spectrumText}>One private home.</span>
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
    <section
      className={styles.mediaRail}
      aria-labelledby="spectrum-story-title"
    >
      <div className={`container ${styles.spectrumStoryHeader}`}>
        <div>
          <p className={styles.kicker}>The product idea, in one picture</p>
          <Heading
            as="h2"
            id="spectrum-story-title"
            className={styles.spectrumStoryTitle}
          >
            One library in.{' '}
            <span className={styles.spectrumText}>Every experience out.</span>
          </Heading>
        </div>
        <p>
          Prismedia keeps the shared shape of your media in one private system,
          then gives movies, music, books, audiobooks, comics, and galleries the
          interfaces they deserve.
        </p>
      </div>
      <div className={`container ${styles.spectrumStoryCanvas}`}>
        <div className={styles.spectrumLegend} aria-hidden>
          <span>White light · shared foundation</span>
          <span>Spectrum · purpose-built media</span>
        </div>
        <PrismFlow />
        <p className={styles.spectrumSupporting}>
          People · Studios · Tags · Collections · Progress · Files · History
        </p>
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

function LaunchFilm() {
  const filmUrl = useBaseUrl('/video/prismedia-launch.mp4');
  const posterUrl = useBaseUrl(
    '/img/showcase/prismedia-launch-poster.webp',
  );
  const videoRef = useRef<HTMLVideoElement>(null);
  const [hasStarted, setHasStarted] = useState(false);

  function playFilm() {
    const playback = videoRef.current?.play();
    if (playback) {
      void playback.catch(() => undefined);
    }
  }

  return (
    <section
      className={styles.launchFilm}
      aria-labelledby="launch-film-title"
    >
      <div className={`container ${styles.launchFilmHeader}`}>
        <div>
          <p className={styles.kicker}>Prismedia in motion · Silent film</p>
          <Heading
            as="h2"
            id="launch-film-title"
            className={styles.displayTitle}
          >
            One library, from first request to every screen.
          </Heading>
        </div>
        <div className={styles.filmMeta} aria-label="Film details">
          <span>01:12</span>
          <span>Silent</span>
          <span>1920 × 1080</span>
        </div>
      </div>

      <div className={`container ${styles.filmStage}`}>
        <div className={styles.filmShell}>
          <div className={styles.filmMedia}>
            <video
              ref={videoRef}
              className={styles.filmVideo}
              controls
              muted
              playsInline
              preload="metadata"
              poster={posterUrl}
              aria-label="Silent Prismedia product film"
              onPlay={() => setHasStarted(true)}
            >
              <source src={filmUrl} type="video/mp4" />
              <p>
                Your browser cannot play this film.{' '}
                <a href={filmUrl}>Download the MP4 instead.</a>
              </p>
            </video>
            {!hasStarted && (
              <button
                className={styles.filmPlayPrompt}
                type="button"
                onClick={playFilm}
                aria-label="Play the 72-second silent Prismedia product film"
              >
                <span className={styles.filmPlayGlyph} aria-hidden>
                  <i />
                </span>
                <span>
                  <strong>Play the product film</strong>
                  <small>72 seconds · silent</small>
                </span>
              </button>
            )}
          </div>
          <div className={styles.filmFooter}>
            <span>Watch · Read · Listen · Request</span>
            <span>Web · iPhone · iPad · Apple TV</span>
          </div>
        </div>
        <div className={styles.filmHandoff} aria-hidden>
          <span />
          <i />
          <i />
          <i />
          <i />
          <i />
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
            <li>Custom native playback with device-level codec support</li>
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
          <span className={styles.platformLabel}>Custom native player · Apple TV</span>
          <Heading as="h3">Full-fidelity playback, built around the device.</Heading>
          <p>
            Across iPhone, iPad, and Apple TV, Prismedia uses the device&apos;s
            own codec and playback stack to direct-play supported sources at
            original quality—including lossless audio—while keeping stream
            state and controls clear.
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
        <div
          className={styles.topology}
          aria-label="One Docker image enters a private Prismedia server and serves the web, iPhone, iPad, and Apple TV experiences."
          role="img"
        >
          <div className={styles.topologyInput}>
            <span>One Docker image</span>
          </div>
          <div className={styles.topologyBeam} aria-hidden />
          <div className={styles.topologyCore}>
            <ProductImage src="/img/logo-mark.png" alt="" />
            <strong>Prismedia</strong>
            <small>private · port 8008</small>
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
        <PrismFlow compact inputLabel="One private library" />
        <p className={styles.kicker}>Bring the whole collection into focus</p>
        <Heading as="h2" className={styles.displayTitle}>
          Self-host the library. Take the{' '}
          <span className={styles.spectrumText}>experience everywhere.</span>
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
      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{__html: JSON.stringify(VIDEO_SCHEMA)}}
      />
      <SectionRoute />
      <Hero />
      <main>
        <MediaRail />
        <Problem />
        <LaunchFilm />
        <Experiences />
        <Platforms />
        <SelfHosting />
        <FinalCta />
      </main>
    </Layout>
  );
}
