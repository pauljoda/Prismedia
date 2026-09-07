import Link from '@docusaurus/Link';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import styles from './legal.module.css';

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
            Get help.
          </Heading>
          <p className={styles.lead}>
            Get help with the Prismedia server, media libraries, native apps,
            playback, reading, listening, or your TestFlight build.
          </p>

          <div className={styles.content}>
            <section className={styles.section}>
              <Heading as="h2">Find the next step</Heading>
              <ul>
                <li><Link to="/docs/getting-started/before-you-install">Setting up for the first time</Link> — what you need and which services are optional.</li>
                <li><Link to="/docs/getting-started/organize-folders">Organizing folders</Link> — Docker mounts, watched roots, and download paths.</li>
                <li><Link to="/docs/using/native-apps">Connecting the native app</Link> — server addresses, sign-in, and network access.</li>
                <li><Link to="/docs/using/requests">Setting up requests</Link> — providers, indexers, download clients, and imports.</li>
                <li><Link to="/docs/advanced/troubleshooting">Something is not working</Link> — start with the symptom and check the relevant error.</li>
              </ul>
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
              <Heading as="h2">Ask the community</Heading>
              <p>
                For setup questions, folder examples, and how other people use
                Prismedia, ask in the subreddit. Describe what you are trying
                to do and where you got stuck.
              </p>
              <div className={styles.actions}>
                <a
                  className={styles.secondaryAction}
                  href="https://www.reddit.com/r/Prismedia/"
                >
                  Visit r/Prismedia
                </a>
              </div>
            </section>

            <section className={styles.section}>
              <Heading as="h2">Report a security issue privately</Heading>
              <p>
                If you suspect unauthorized access or exposure of private data,
                use the private reporting channel. Keep vulnerability details out
                of public issues and community posts.
              </p>
              <div className={styles.actions}>
                <a className={styles.secondaryAction} href="https://github.com/pauljoda/Prismedia/security/advisories/new">Report a vulnerability</a>
                <Link className={styles.secondaryAction} to="/docs/deployment/security">Security policy and acknowledgments</Link>
              </div>
            </section>

            <section className={styles.section}>
              <Heading as="h2">Report a problem or suggest a change</Heading>
              <p>
                Search existing GitHub issues first. If the problem has not been
                reported, choose the form that fits. Native app reports belong
                here too; include both the app build and server version.
              </p>
              <div className={styles.actions}>
                <a className={styles.secondaryAction} href="https://github.com/pauljoda/Prismedia/issues/new?template=bug-report.yml">Report a bug</a>
                <a className={styles.secondaryAction} href="https://github.com/pauljoda/Prismedia/issues/new?template=documentation.yml">Improve a guide</a>
                <a className={styles.secondaryAction} href="https://github.com/pauljoda/Prismedia/issues/new?template=feature-request.yml">Suggest a feature</a>
              </div>
              <Heading as="h3">Useful details to include</Heading>
              <ul>
                <li>The running versions, device or browser, and image channel.</li>
                <li>Steps to reproduce, the expected result, and what happened.</li>
                <li>The relevant job error or a short excerpt of container logs.</li>
              </ul>
              <p>Remove passwords, access tokens, private hostnames, and private media details from logs and screenshots before posting.</p>
            </section>
          </div>
        </div>
      </main>
    </Layout>
  );
}
