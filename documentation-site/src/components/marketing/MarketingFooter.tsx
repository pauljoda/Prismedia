import Link from '@docusaurus/Link';
import useBaseUrl from '@docusaurus/useBaseUrl';
import ArrowIcon from './ArrowIcon';
import BrandLink from './BrandLink';
import {APP_STORE_URL, GITHUB_URL, REDDIT_URL, TESTFLIGHT_URL} from './links';
import styles from './MarketingFooter.module.css';

/** The homepage's closing invitation and practical next steps. */
export default function MarketingFooter() {
  return <footer className={styles.footer} aria-labelledby="community-title">
    <div className={styles.inner}>
      <div className={styles.community}>
        <div className={styles.introduction}>
          <div className={styles.brand}><img src={useBaseUrl('/img/logo-mark.png')} alt="" width={640} height={590} /><span>Prismedia</span></div>
          <h2 id="community-title">A project you can be part of.</h2>
          <p>Prismedia is built in the open. Explore the source, share how you use your library, or tell us what could work better.</p>
        </div>
        <div className={styles.communityLinks}>
          <BrandLink href={GITHUB_URL} icon="/img/brands/github.svg"><span><strong>View on GitHub</strong><small>Source code, development, and issues</small></span><ArrowIcon diagonal /></BrandLink>
          <BrandLink href={REDDIT_URL} icon="/img/brands/reddit.svg"><span><strong>r/Prismedia</strong><small>Questions, ideas, and conversations</small></span><ArrowIcon diagonal /></BrandLink>
        </div>
      </div>
      <nav className={styles.resources} aria-label="Footer navigation">
        <div><h3>Start your library</h3><Link to="/docs/getting-started/install">Install with Docker <ArrowIcon /></Link><Link to="/docs/getting-started/organize-folders">Organize your folders <ArrowIcon /></Link><Link to="/docs/getting-started/first-library">Add your first library <ArrowIcon /></Link></div>
        <div><h3>Using Prismedia</h3><Link to="/docs/using/playback">Watch, listen, and read <ArrowIcon /></Link><Link to="/docs/using/requests">Connect your acquisition tools <ArrowIcon /></Link><Link href={`${GITHUB_URL}/blob/main/CHANGELOG.md`}>Read the release notes <ArrowIcon diagonal /></Link></div>
        <div className={styles.downloads}><h3>Native apps</h3><BrandLink href={APP_STORE_URL} icon="/img/brands/app-store.svg">Apple TV on the App Store</BrandLink><BrandLink href={TESTFLIGHT_URL} icon="/img/brands/testflight.png">Early builds on TestFlight</BrandLink></div>
      </nav>
      <div className={styles.bottom}><span>© {new Date().getFullYear()} Prismedia</span><div><Link to="/support">Support</Link><Link to="/privacy">Privacy</Link></div></div>
    </div>
  </footer>;
}
