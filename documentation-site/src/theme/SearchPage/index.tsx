import Head from '@docusaurus/Head';
import SearchPage from '@theme-original/SearchPage';

/** Search is a navigation tool; only the source guides belong in search engines. */
export default function DocumentationSearchPage() {
  return <><Head><meta name="robots" content="noindex, follow" /></Head><SearchPage /></>;
}
