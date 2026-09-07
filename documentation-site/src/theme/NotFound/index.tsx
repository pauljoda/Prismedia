import Head from '@docusaurus/Head';
import NotFound from '@theme-original/NotFound';

/** Preserve the standard recovery page while keeping it out of search results. */
export default function NotFoundPage() {
  return <>
    <NotFound />
    <Head><meta name="robots" content="noindex, nofollow" /></Head>
  </>;
}
