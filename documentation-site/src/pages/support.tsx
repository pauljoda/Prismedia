import Link from '@docusaurus/Link';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import styles from './legal.module.css';

const SUPPORT_EMAIL = 'pauldavis101@gmail.com';

export default function SupportPage() {
  return (
    <Layout
      title="Support"
      description="Get help installing, configuring, or using Prismedia on the web, iPhone, iPad, and Apple TV."
    >
      <main className={styles.page}>
        <div className={styles.shell}>
          <p className={styles.eyebrow}>Prismedia support</p>
          <Heading as="h1" className={styles.title}>
            Keep your library moving.
          </Heading>
          <p className={styles.lead}>
            Get help with the Prismedia server, media libraries, native apps,
            playback, reading, listening, or your TestFlight build.
          </p>

          <div className={styles.content}>
            <section className={styles.section}>
              <Heading as="h2">Start with the docs</Heading>
              <p>
                Installation, first-library setup, reverse proxies, backups,
                upgrades, playback, reading, requests, and troubleshooting are
                covered in the task-oriented documentation.
              </p>
              <div className={styles.actions}>
                <Link className={styles.primaryAction} to="/docs/intro">
                  Read the documentation
                </Link>
                <Link
                  className={styles.secondaryAction}
                  to="/docs/advanced/troubleshooting"
                >
                  Troubleshoot Prismedia
                </Link>
              </div>
            </section>

            <section className={styles.section}>
              <Heading as="h2">Contact support</Heading>
              <p>
                For account, TestFlight, or private support questions, email{' '}
                <a href={`mailto:${SUPPORT_EMAIL}`}>{SUPPORT_EMAIL}</a>. Include
                the platform, app version, server version, and the exact action
                that did not work. Do not send passwords, access tokens, or
                private media.
              </p>
              <p>
                For reproducible bugs and feature requests that can be discussed
                publicly, use GitHub Issues so the resolution can help other
                self-hosters.
              </p>
              <div className={styles.actions}>
                <a
                  className={styles.primaryAction}
                  href={`mailto:${SUPPORT_EMAIL}?subject=Prismedia%20Support`}
                >
                  Email support
                </a>
                <a
                  className={styles.secondaryAction}
                  href="https://github.com/pauljoda/Prismedia/issues"
                >
                  Open GitHub Issues
                </a>
              </div>
            </section>

            <section className={styles.section}>
              <Heading as="h2">Before you write</Heading>
              <ul>
                <li>Confirm the server is reachable from the device.</li>
                <li>Update the server and native app to the latest available build.</li>
                <li>Retry once after signing out and back in.</li>
                <li>
                  Share relevant error text and safe diagnostics, but redact host
                  details if they identify a private network.
                </li>
              </ul>
            </section>
          </div>
        </div>
      </main>
    </Layout>
  );
}
