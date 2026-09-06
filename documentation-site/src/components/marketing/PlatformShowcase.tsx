import useBaseUrl from '@docusaurus/useBaseUrl';
import ProductScreenshot from './ProductScreenshot';
import styles from './PlatformShowcase.module.css';

function Screen({src, alt, width, height, className, label, sizes, priority = false}: {src: string; alt: string; width: number; height: number; className: string; label: string; sizes: string; priority?: boolean}) {
  const url = useBaseUrl(`/img/showcase/${src}`);
  return <figure className={className}>
    <figcaption>{label}</figcaption>
    <a href={url} target="_blank" rel="noopener noreferrer" aria-label={`${alt}. Open full-size screenshot`}>
      <ProductScreenshot src={`/img/showcase/${src}`} alt={alt} width={width} height={height} sizes={sizes} eager priority={priority} />
    </a>
  </figure>;
}

/** A layered view of the real web and native apps, with full-resolution images one tap away. */
export default function PlatformShowcase() {
  return <div className={styles.showcase} aria-label="Prismedia on web, iPhone, and Apple TV">
    <Screen src="tvos-movies-live.webp" alt="Browse a movie collection in the native Apple TV app" label="Apple TV" width={3840} height={2160} className={styles.tv} sizes="(max-width: 760px) 47vw, (max-width: 1136px) 43vw, 447px" />
    <Screen src="web-movies-live.webp" alt="Browse a movie collection in the Prismedia web app" label="Web" width={2880} height={1800} className={styles.web} sizes="(max-width: 760px) calc(100vw - 36px), (max-width: 1136px) calc(100vw - 96px), 1040px" priority />
    <Screen src="ios-book-cover-live.webp" alt="Explore a book in the native iPhone app" label="iPhone" width={1206} height={2622} className={styles.phone} sizes="(max-width: 760px) 25vw, (max-width: 1136px) 20vw, 208px" />
  </div>;
}
