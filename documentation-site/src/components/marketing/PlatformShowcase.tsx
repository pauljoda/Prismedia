import useBaseUrl from '@docusaurus/useBaseUrl';
import styles from './PlatformShowcase.module.css';

function Screen({src, alt, width, height, className, label}: {src: string; alt: string; width: number; height: number; className: string; label: string}) {
  const url = useBaseUrl(`/img/showcase/${src}`);
  return <figure className={className}>
    <figcaption>{label}</figcaption>
    <a href={url} target="_blank" rel="noopener noreferrer" aria-label={`${alt}. Open full-size screenshot`}>
      <img src={url} alt={alt} width={width} height={height} loading="eager" decoding="async" />
    </a>
  </figure>;
}

/** A layered view of the real web and native apps, with full-resolution images one tap away. */
export default function PlatformShowcase() {
  return <div className={styles.showcase} aria-label="Prismedia on web, iPhone, and Apple TV">
    <Screen src="tvos-movies-live.webp" alt="Browse a movie collection in the native Apple TV app" label="Apple TV" width={3840} height={2160} className={styles.tv} />
    <Screen src="web-movies-live.webp" alt="Browse a movie collection in the Prismedia web app" label="Web" width={2880} height={1800} className={styles.web} />
    <Screen src="ios-book-cover-live.webp" alt="Explore a book in the native iPhone app" label="iPhone" width={1206} height={2622} className={styles.phone} />
  </div>;
}
