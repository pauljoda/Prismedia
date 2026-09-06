import {useEffect, type CSSProperties, type ReactNode} from 'react';
import Link from '@docusaurus/Link';
import useBaseUrl from '@docusaurus/useBaseUrl';
import {useLocation} from '@docusaurus/router';
import Layout from '@theme/Layout';
import ArrowIcon from '../components/marketing/ArrowIcon';
import PrismStory from '../components/marketing/PrismStory';
import {ENTITY_KIND, MEDIA_FAMILIES, familyStyle} from '../components/marketing/media-families';
import styles from './index.module.css';

const TESTFLIGHT_URL = 'https://testflight.apple.com/join/c9bgDxr7';
const APP_STORE_URL = 'https://apps.apple.com/us/app/prismedia/id6792944211';
const GITHUB_URL = 'https://github.com/pauljoda/Prismedia';
const TITLE = 'A clear home for all your media.';
const DESCRIPTION = 'Bring movies, series, music, books, audiobooks, comics, images, and galleries into one private, self-hosted library. Find, organize, watch, listen, and read with Prismedia.';
const PRODUCT_SCHEMA = {
  '@context': 'https://schema.org',
  '@type': 'SoftwareApplication',
  name: 'Prismedia',
  applicationCategory: 'MultimediaApplication',
  operatingSystem: 'Web, iOS, iPadOS, tvOS',
  description: DESCRIPTION,
  url: 'https://pauljoda.github.io/Prismedia/',
  image: 'https://pauljoda.github.io/Prismedia/img/prismedia-social-card.png',
  softwareRequirements: 'A self-hosted Prismedia server; Docker for server installation',
  license: `${GITHUB_URL}/blob/main/LICENSE`,
  isAccessibleForFree: true,
  sameAs: [GITHUB_URL, APP_STORE_URL, TESTFLIGHT_URL],
};
const SECTION_IDS = new Set(['product', 'experiences', 'platforms', 'self-hosting']);

// Preserve existing links from the documentation navigation and published material.
function SectionRoute() {
  const location = useLocation();
  const homeUrl = useBaseUrl('/');
  useEffect(() => {
    const section = new URLSearchParams(location.search).get('section');
    if (!section || !SECTION_IDS.has(section)) return;
    const frame = window.requestAnimationFrame(() => {
      document.getElementById(section)?.scrollIntoView();
      window.history.replaceState(window.history.state, '', `${homeUrl}#${section}`);
    });
    return () => window.cancelAnimationFrame(frame);
  }, [homeUrl, location.search]);
  return null;
}

function ProductImage({src, alt, className, eager = false, width = 1280, height = 720}: {
  src: string; alt: string; className?: string; eager?: boolean; width?: number; height?: number;
}) {
  return <img src={useBaseUrl(src)} alt={alt} width={width} height={height} className={className} loading={eager ? 'eager' : 'lazy'} decoding="async" />;
}

function Frame({children, label, className = ''}: {children: ReactNode; label: string; className?: string}) {
  return <figure className={`${styles.frame} ${className}`}><div className={styles.frameBar}><span className={styles.windowDots} aria-hidden="true"><i /><i /><i /></span><span>{label}</span></div>{children}</figure>;
}

function Phone({src, alt, className = ''}: {src: string; alt: string; className?: string}) {
  return <figure className={`${styles.phone} ${className}`}><ProductImage src={src} alt={alt} width={1320} height={2868} /></figure>;
}

function MediaLabel({children, style}: {children: ReactNode; style?: CSSProperties}) {
  return <p className={styles.mediaLabel} style={style}>{style && <i aria-hidden="true" />}{children}</p>;
}

function Hero() {
  return <header className={`${styles.wrap} ${styles.hero}`}>
    <h1>A clear home for <br />all your media.</h1>
    <p className={styles.lede}>Movies, music, books, and everything between.<br />Find, organize, and enjoy your collection in one self-hosted app.</p>
    <div className={styles.actions}>
      <a className={styles.primaryAction} href="#product">See how it comes together <ArrowIcon down /></a>
      <Link className={styles.textLink} to="/docs/getting-started/install">Read the setup guide <ArrowIcon /></Link>
    </div>
    <div className={styles.heroProduct}>
      <Frame label="Your Prismedia library" className={styles.heroDesktop}><ProductImage src="/img/showcase/web-dashboard.webp" alt="Prismedia dashboard with books, video, and music in one library" eager /></Frame>
      <Phone src="/img/showcase/ios-book-combined.webp" alt="A book's reading and listening experiences in the native iPhone app" className={styles.heroPhone} />
      <p className={styles.heroCaption}>At home on the web. Made for your devices.</p>
    </div>
    <ul className={styles.mediaRail} aria-label="Explore supported media">
      {MEDIA_FAMILIES.filter((family) => family.kind !== ENTITY_KIND.collection).map((family) => <li key={family.kind} style={family.style}><Link to={family.href}><i aria-hidden="true" />{family.label}</Link></li>)}
    </ul>
  </header>;
}

function FounderStory() {
  return <section className={`${styles.wrap} ${styles.founder}`} aria-labelledby="founder-title">
    <div><h2 id="founder-title">The same work,<br />in too many places.</h2></div>
    <div className={styles.founderCopy}>
      <p className={styles.storyLead}>“I kept finding the same pattern: discover something, identify it, bring it into the library, then enjoy it.”</p>
      <p>Movies and TV made that pattern familiar. As I added music and books, I found myself maintaining separate tools that repeated much of the same work. I wanted one place to manage the collection, with an interface that felt considered on every screen.</p>
      <p>That became Prismedia. Each item is an <strong>Entity</strong>: its identity, files, relationships, and history stay together. The shared foundation gives each medium room for its own experience.</p>
      <p className={styles.signature}>Paul <span>Creator of Prismedia</span></p>
    </div>
  </section>;
}

function Workflow() {
  const steps = [
    ['01', 'Start with your files', 'Add a media folder and let Prismedia scan it into your library. Begin with a few titles; add the rest when you are ready.'],
    ['02', 'Give each item its details', 'Review metadata and artwork from your chosen providers. People, series, and collections connect the library.'],
    ['03', 'Make yourself at home', 'Watch a film, put on an album, or open a book. Return to the things you are enjoying with personal progress.'],
  ];
  return <section className={`${styles.wrap} ${styles.section}`} aria-labelledby="workflow-title">
    <h2 id="workflow-title">A familiar way in.</h2>
    <div className={styles.steps}>{steps.map(([number, title, copy]) => <article key={number}><span className={styles.stepNumber}>{number}</span><h3>{title}</h3><p>{copy}</p></article>)}</div>
    <Link className={styles.textLink} to="/docs/getting-started/first-library">Walk through your first library <ArrowIcon /></Link>
  </section>;
}

function Experiences() {
  return <section id="experiences" className={`${styles.wrap} ${styles.section}`} aria-labelledby="experiences-title">
    <h2 id="experiences-title">A library is also<br />how you spend time in it.</h2>
    <div className={styles.experiences}>
      <article className={styles.watchExperience}>
        <div className={styles.experienceCopy}><h3>Sit down with a film.<br />Stay for the next episode.</h3><MediaLabel style={familyStyle(ENTITY_KIND.movie)}>Movies &amp; series</MediaLabel><p>Artwork and details help you find something to watch. Subtitles, playback controls, and your place in the story stay close at hand.</p><Link className={styles.textLink} to="/docs/using/playback">Explore video playback <ArrowIcon /></Link></div>
        <Frame label="A movie in your library"><ProductImage src="/img/showcase/web-detail.webp" alt="Prismedia movie detail with artwork, metadata, and playback actions" /></Frame>
      </article>
      <article className={styles.readExperience}>
        <div className={styles.experienceCopy}><h3>The same book.<br />Two ways into it.</h3><MediaLabel style={familyStyle(ENTITY_KIND.book)}>Books, comics &amp; audiobooks</MediaLabel><p>Make the page comfortable, or settle into the audiobook. Keep text and audio on one book page, with separate reading and listening positions.</p><Link className={styles.textLink} to="/docs/library/books">Explore reading and listening <ArrowIcon /></Link></div>
        <div className={styles.readingPhones}><Phone src="/img/showcase/ios-reader.webp" alt="The native reader with adjustable typography and page appearance" /><Phone src="/img/showcase/ios-book-combined.webp" alt="A book with separate Continue Reading and Continue Listening actions" /></div>
      </article>
      <article className={styles.musicExperience}>
        <div className={styles.experienceCopy}><h3>Put something on.<br />Let it play.</h3><MediaLabel style={familyStyle(ENTITY_KIND.audio)}>Artists, albums &amp; tracks</MediaLabel><p>Browse your albums, build a queue, and keep listening as you move through the library.</p><Link className={styles.textLink} to="/docs/library/audio">Explore your music library <ArrowIcon /></Link></div>
        <Phone src="/img/showcase/ios-music-player.webp" alt="Prismedia's native music player with album artwork and playback controls" />
      </article>
      <article className={styles.galleryExperience}>
        <div className={styles.experienceCopy}><h3>Room to look<br />a little closer.</h3><MediaLabel style={familyStyle(ENTITY_KIND.gallery)}>Images &amp; galleries</MediaLabel><p>Browse images, move through galleries, and organize visual collections with people and tags.</p><Link className={styles.textLink} to="/docs/library/images-galleries">Explore images and galleries <ArrowIcon /></Link></div>
        <Frame label="Galleries"><ProductImage src="/img/screenshots/galleries.png" alt="Prismedia gallery library with artwork and collection details" /></Frame>
      </article>
    </div>
  </section>;
}

function Acquisition() {
  return <section className={`${styles.wrap} ${styles.section} ${styles.split}`} aria-labelledby="acquisition-title">
    <div><h2 id="acquisition-title">From finding it<br />to having it here.</h2><p className={styles.bodyCopy}>Connect the indexers and download clients you already use. Search through metadata providers, review a release, and follow it into the library. Prismedia handles acquisition and imports while keeping the item's identity and history together.</p><Link className={styles.textLink} to="/docs/using/requests">Understand requests and acquisition <ArrowIcon /></Link></div>
    <ol className={styles.acquisitionFlow}>
      <li><span>01</span><div><h3>Find a title</h3><p>Your metadata providers</p></div></li>
      <li><span>02</span><div><h3>Choose a release</h3><p>Your indexers</p></div></li>
      <li><span>03</span><div><h3>Follow the download</h3><p>Your download clients</p></div></li>
      <li><span>04</span><div><h3>Open it in your library</h3><p>Prismedia verifies and imports the files</p></div></li>
    </ol>
  </section>;
}

function Platforms() {
  return <section id="platforms" className={`${styles.wrap} ${styles.section}`} aria-labelledby="platforms-title">
    <div className={styles.sectionHeading}><div><h2 id="platforms-title">One collection.<br />A considered experience on each.</h2></div><p>The responsive web app and native Apple apps connect to your Prismedia server. Each screen has room to work the way it should.</p></div>
    <div className={styles.platformGrid}>
      <article><span className={styles.platformLabel}>Web</span><h3>The whole library<br />in your browser.</h3><p>Browse and enjoy your media, manage files, identify titles, and follow background work. The layout adapts from desktop to phone.</p><Link className={styles.textLink} to="/docs/getting-started/install">Set up your server <ArrowIcon /></Link></article>
      <article><span className={styles.platformLabel}>iPhone &amp; iPad</span><h3>Made for touch.<br />Ready for a good book.</h3><p>Native browsing, playback, reading, and listening, with controls shaped for your device.</p><Link className={styles.textLink} href={TESTFLIGHT_URL}>Test early builds <ArrowIcon /></Link></article>
      <article><span className={styles.platformLabel}>Apple TV</span><h3>Your collection,<br />from the couch.</h3><p>A focus-based interface and native video player for the biggest screen in the house.</p><Link className={styles.textLink} href={APP_STORE_URL}>Get the Apple TV app <ArrowIcon diagonal /></Link></article>
    </div>
    <p className={styles.platformNote}>Native apps need a reachable Prismedia server and your account. TestFlight is available for testing early builds.</p>
  </section>;
}

function ProductFilm() {
  const film = useBaseUrl('/video/prismedia-launch.mp4');
  const poster = useBaseUrl('/img/showcase/prismedia-launch-poster.webp');
  return <section className={`${styles.wrap} ${styles.filmSection}`} aria-labelledby="film-title">
    <div><h2 id="film-title">Spend a moment<br />inside the library.</h2><p>A 72-second visual tour of Prismedia on the web and native Apple apps. Play it when you are ready.</p><span className={styles.utility}>72 seconds · Silent product tour</span></div>
    <video controls muted playsInline preload="none" poster={poster} aria-label="72-second silent Prismedia product tour"><source src={film} type="video/mp4" /><p><a href={film}>Download the product tour.</a></p></video>
  </section>;
}

function SelfHosting() {
  return <section id="self-hosting" className={`${styles.wrap} ${styles.section} ${styles.split}`} aria-labelledby="hosting-title">
    <div><h2 id="hosting-title">Start with the collection<br />you already have.</h2><p className={styles.bodyCopy}>Run Prismedia with Docker, make your media folders accessible, and add your first watched root. The guide explains which path to enter and what each folder becomes.</p><div className={styles.actions}><Link className={styles.primaryAction} to="/docs/getting-started/install">Set up Prismedia <ArrowIcon diagonal /></Link><Link className={styles.textLink} to="/docs/getting-started/organize-folders">Understand your folders <ArrowIcon /></Link></div></div>
    <div className={styles.folderExample}><span className={styles.utility}>An example collection</span><pre><code>{`/media/
├── movies/
├── tv/
├── music/
├── books/
├── comics/
└── images/`}</code></pre><p>Add each media folder as a watched root.<br />Start with the kinds of media you have.</p></div>
  </section>;
}

function Project() {
  return <section className={`${styles.wrap} ${styles.project}`} aria-labelledby="project-title">
    <ProductImage src="/img/logo-mark.png" alt="" className={styles.projectMark} width={640} height={590} />
    <div><h2 id="project-title">A personal library.<br />A project you can be part of.</h2><p>Prismedia is free for noncommercial use, with its source available to read, learn from, and contribute to under the project's license.</p><div className={styles.projectLinks}><Link href={GITHUB_URL}>Explore the source <ArrowIcon diagonal /></Link><Link href={`${GITHUB_URL}/blob/main/LICENSE`}>Read the license <ArrowIcon diagonal /></Link><Link href="https://www.reddit.com/r/Prismedia/">Join the conversation <ArrowIcon diagonal /></Link></div></div>
  </section>;
}

export default function Home(): ReactNode {
  return <Layout title={TITLE} description={DESCRIPTION}>
    <script type="application/ld+json" dangerouslySetInnerHTML={{__html: JSON.stringify(PRODUCT_SCHEMA)}} />
    <SectionRoute />
    <main className={styles.page}>
      <Hero /><PrismStory /><FounderStory /><Workflow /><Experiences /><Acquisition /><Platforms /><ProductFilm /><SelfHosting /><Project />
    </main>
  </Layout>;
}
