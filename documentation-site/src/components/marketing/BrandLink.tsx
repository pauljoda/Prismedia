import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';
import useBaseUrl from '@docusaurus/useBaseUrl';
import styles from './BrandLink.module.css';

/** A familiar service icon and destination label on the site's standard neutral action. */
export default function BrandLink({href, icon, children, primary = false}: {href: string; icon: string; children: ReactNode; primary?: boolean}) {
  return <Link href={href} className={`marketing-glass ${primary ? 'marketing-prism' : ''} ${styles.link}`}>
    <img src={useBaseUrl(icon)} alt="" width={20} height={20} className={icon.endsWith('.svg') ? styles.monochrome : undefined} />
    {children}
  </Link>;
}
