import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  docsSidebar: [
    'intro',
    {
      type: 'category',
      label: 'Getting Started',
      collapsed: false,
      items: [
        'getting-started/before-you-install',
        'getting-started/install',
        'getting-started/organize-folders',
        'getting-started/first-library',
        'getting-started/identify-walkthrough',
      ],
    },
    'advanced/troubleshooting',
    {
      type: 'category',
      label: 'Using Prismedia',
      collapsed: true,
      items: [
        'using/browsing',
        'using/playback',
        'using/native-apps',
        'using/read-and-listen',
        'using/reader-settings',
        'using/music-player',
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
      collapsed: true,
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
      label: 'Deployment & Security',
      collapsed: true,
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
        'developers/entity-definitions-and-data-flow',
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
      ],
    },
  ],
};

export default sidebars;
