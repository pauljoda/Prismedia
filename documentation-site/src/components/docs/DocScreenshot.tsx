import useBaseUrl from '@docusaurus/useBaseUrl';
import type {ReactNode} from 'react';
import styles from './DocScreenshot.module.css';

interface DocScreenshotProps {
  src: string;
  alt: string;
  width: number;
  height: number;
  caption?: string;
}

/** Keep related mobile captures together without making each fill the article. */
export function DocScreenshotGrid({children}: {children: ReactNode}) {
  return <div className={styles.grid}>{children}</div>;
}

/** A real product capture with reserved layout space and a full-resolution link. */
export default function DocScreenshot({src, alt, width, height, caption}: DocScreenshotProps) {
  const url = useBaseUrl(src);
  return (
    <figure className={`${styles.figure} ${height > width * 1.4 ? styles.portrait : ''}`}>
      <a className={styles.link} href={url} target="_blank" rel="noopener noreferrer" aria-label={`Open full-size screenshot: ${alt}`}>
        <img src={url} alt={alt} width={width} height={height} loading="lazy" decoding="async" />
        <span className={styles.expand} aria-hidden="true">↗</span>
      </a>
      {caption && <figcaption>{caption}</figcaption>}
    </figure>
  );
}
