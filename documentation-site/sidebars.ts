import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  docsSidebar: [
    'intro',
    {
      type: 'category',
      label: 'Getting Started',
      collapsed: false,
      items: [
        'getting-started/install',
        'getting-started/first-library',
        'getting-started/identify-walkthrough',
      ],
    },
    {
      type: 'category',
      label: 'Using Prismedia',
      collapsed: false,
      items: [
        'using/browsing',
        'using/playback',
        'using/navigation',
        'using/identify',
        'using/requests',
        'using/collections',
        'using/jobs',
        'using/settings',
      ],
    },
    {
      type: 'category',
      label: 'Library & Scanning',
      collapsed: false,
      items: [
        'library/overview',
        'library/videos',
        'library/images-galleries',
        'library/books',
        'library/opds',
        'library/audio',
      ],
    },
    {
      type: 'category',
      label: 'Jellyfin Clients',
      collapsed: false,
      items: [
        'jellyfin/overview',
        'jellyfin/profiles',
        'jellyfin/clients',
      ],
    },
    {
      type: 'category',
      label: 'Deployment & Security',
      collapsed: false,
      items: [
        'deployment/authentication',
        'deployment/reverse-proxy',
        'deployment/backups',
        'deployment/upgrading',
      ],
    },
    {
      type: 'category',
      label: 'Developers',
      collapsed: true,
      items: [
        'developers/architecture',
        'developers/codebase-flow',
        'developers/monorepo',
        'developers/database',
        'developers/api-and-jobs',
        'developers/hls-streaming',
        'developers/design-language',
        'developers/contributing',
      ],
    },
    {
      type: 'category',
      label: 'Plugins',
      collapsed: true,
      items: [
        'plugins/overview',
        'plugins/manifest',
        'plugins/capabilities',
        'plugins/stash-compat',
        'plugins/publishing',
      ],
    },
    {
      type: 'category',
      label: 'Advanced',
      collapsed: true,
      items: [
        'advanced/stash-compatibility',
        'advanced/troubleshooting',
      ],
    },
  ],
};

export default sidebars;
