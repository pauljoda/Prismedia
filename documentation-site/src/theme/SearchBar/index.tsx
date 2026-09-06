import {useLocation} from '@docusaurus/router';
import useBaseUrl from '@docusaurus/useBaseUrl';
import SearchBar from '@theme-original/SearchBar';

/** Keep documentation search in the docs without changing the marketing header. */
export default function DocumentationSearchBar() {
  const {pathname} = useLocation();
  const docsPath = useBaseUrl('/docs/');
  const searchPath = useBaseUrl('/search');
  return pathname.startsWith(docsPath) || pathname === searchPath ? <SearchBar /> : null;
}
