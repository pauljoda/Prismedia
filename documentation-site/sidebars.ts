import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  docsSidebar: [
    'intro',
    {
      type: 'category',
      label: 'Users',
      collapsed: false,
      items: [
        'users/quick-start',
        'users/first-boot',
        'users/library-organization',
        'users/browsing',
        'users/playback',
        'users/identify-and-scrape',
        'users/operations',
        'users/settings',
        'users/upgrading',
      ],
    },
    {
      type: 'category',
      label: 'Developers',
      collapsed: false,
      items: [
        'developers/architecture',
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
      collapsed: false,
      items: [
        'plugins/overview',
        'plugins/manifest',
        'plugins/capabilities',
        'plugins/typescript-plugin',
        'plugins/python-plugin',
        'plugins/stash-compat',
        'plugins/publishing',
      ],
    },
    {
      type: 'category',
      label: 'Advanced',
      collapsed: false,
      items: [
        'advanced/phash-contribution',
        'advanced/stashbox',
        'advanced/troubleshooting',
      ],
    },
  ],
};

export default sidebars;
