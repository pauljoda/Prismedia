import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import styles from './legal.module.css';

const SUPPORT_EMAIL = 'pauldavis101@gmail.com';

export default function PrivacyPage() {
  return (
    <Layout
      title="Privacy Policy"
      description="How Prismedia handles server credentials, library activity, and device data."
    >
      <main className={styles.page}>
        <div className={styles.shell}>
          <p className={styles.eyebrow}>Privacy policy</p>
          <Heading as="h1" className={styles.title}>
            Your media stays yours.
          </Heading>
          <p className={styles.lead}>
            Prismedia is a client for a server you choose and operate. The app
            has no advertising, cross-app tracking, or developer analytics.
          </p>
          <p className={styles.updated}>Effective July 25, 2026</p>

          <div className={styles.content}>
            <section className={styles.section}>
              <Heading as="h2">Summary</Heading>
              <p>
                Prismedia does not sell personal information and does not use
                third-party advertising or tracking SDKs. When you connect the
                app to your self-hosted Prismedia server, the app communicates
                directly with that server. The server operator—not the
                Prismedia app developer—controls the server, its media, user
                accounts, logs, backups, and connected integrations.
              </p>
            </section>

            <section className={styles.section}>
              <Heading as="h2">Information the app handles</Heading>
              <p>
                To provide its features, the app handles the server address,
                username, password during sign-in, the resulting session token,
                library metadata and artwork, media streams, and personal
                activity such as playback or reading progress. This information
                is requested from or sent to the Prismedia server you select.
              </p>
              <p>
                Sign-in tokens are stored in the device Keychain. The selected
                server and interface preferences are stored on the device. The
                app may use system media and network features needed to connect
                to your server and play, display, or download the content you
                request.
              </p>
            </section>

            <section className={styles.section}>
              <Heading as="h2">Data collection by the developer</Heading>
              <p>
                The released app does not transmit analytics, advertising
                identifiers, contacts, location, photos, or library activity to
                the developer. Information exchanged solely with a server that
                you or your household operates is not received or controlled by
                the developer.
              </p>
              <p>
                If you contact support, the developer receives the information
                you choose to include in that message and uses it only to
                respond, investigate, and maintain an appropriate support
                history. Do not send media, passwords, or access tokens.
              </p>
            </section>

            <section className={styles.section}>
              <Heading as="h2">Test and review servers</Heading>
              <p>
                A limited beta or platform-review build may be given access to a
                dedicated demonstration server operated by the developer. That
                server contains licensed demonstration media rather than a
                private household library. Sign-in and in-app activity can be
                recorded in normal server security and application logs for
                reliability, abuse prevention, and review support. The
                demonstration environment is separate from production household
                servers and can be reset or removed after the test period.
              </p>
            </section>

            <section className={styles.section}>
              <Heading as="h2">Third-party services</Heading>
              <p>
                A server operator may configure metadata providers, indexers,
                download clients, reverse proxies, or other integrations. Those
                services are selected and controlled on the server and are
                governed by their own privacy terms. Prismedia does not enable
                them in the native app on the operator&apos;s behalf.
              </p>
            </section>

            <section className={styles.section}>
              <Heading as="h2">Retention and deletion</Heading>
              <p>
                Remove the server from the app or uninstall the app to remove
                locally stored Prismedia configuration and credentials. To
                delete an account, playback history, reading progress, or other
                server data, contact the operator of the server you use. The
                developer cannot access or delete data on a self-hosted server
                the developer does not operate.
              </p>
            </section>

            <section className={styles.section}>
              <Heading as="h2">Children</Heading>
              <p>
                Prismedia is not directed to children and does not knowingly
                collect personal information from children. A household server
                operator is responsible for the accounts and content made
                available through that server.
              </p>
            </section>

            <section className={styles.section}>
              <Heading as="h2">Changes and contact</Heading>
              <p>
                This policy may be updated as Prismedia&apos;s features or
                distribution change. The effective date above identifies the
                current version. Questions about this policy can be sent to{' '}
                <a href={`mailto:${SUPPORT_EMAIL}`}>{SUPPORT_EMAIL}</a>.
              </p>
            </section>
          </div>
        </div>
      </main>
    </Layout>
  );
}
