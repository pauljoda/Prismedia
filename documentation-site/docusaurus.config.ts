import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';
import type {PrismTheme} from 'prism-react-renderer';

const prismediaPrismTheme: PrismTheme = {
  plain: {
    color: '#c4c9d4',
    backgroundColor: 'transparent',
  },
  styles: [
    {types: ['comment', 'prolog', 'doctype', 'cdata'], style: {color: '#5a6378', fontStyle: 'italic' as const}},
    {types: ['punctuation'], style: {color: '#8a93a6'}},
    {types: ['property', 'tag', 'constant', 'symbol', 'deleted'], style: {color: '#b3484d'}},
    {types: ['boolean', 'number'], style: {color: '#b76337'}},
    {types: ['selector', 'attr-name', 'string', 'char', 'builtin', 'inserted'], style: {color: '#4d925d'}},
    {types: ['operator', 'entity', 'url', 'variable'], style: {color: '#c4c9d4'}},
    {types: ['atrule', 'attr-value', 'function', 'class-name'], style: {color: '#3b869c'}},
    {types: ['keyword'], style: {color: '#775ca5'}},
    {types: ['regex', 'important'], style: {color: '#9e873b'}},
  ],
};

// This runs in Node.js - Don't use client-side code here (browser APIs, JSX...)

const config: Config = {
  title: 'Prismedia',
  tagline: 'A private, self-hosted home for your entire media collection.',
  favicon: 'img/favicon-32.png',

  // Future flags, see https://docusaurus.io/docs/api/docusaurus-config#future
  future: {
    v4: true, // Improve compatibility with the upcoming Docusaurus v4
  },
  markdown: {
    mermaid: true,
  },
  themes: ['@docusaurus/theme-mermaid'],

  // Set the production url of your site here
  url: 'https://pauljoda.github.io',
  // Set the /<baseUrl>/ pathname under which your site is served
  // For GitHub pages deployment, it is often '/<projectName>/'
  baseUrl: '/Prismedia/',

  // GitHub pages deployment config.
  // If you aren't using GitHub pages, you don't need these.
  organizationName: 'pauljoda',
  projectName: 'Prismedia',

  onBrokenLinks: 'throw',
  trailingSlash: false,

  // Even if you don't use internationalization, you can use this field to set
  // useful metadata like html lang. For example, if your site is Chinese, you
  // may want to replace "en" with "zh-Hans".
  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          editUrl:
            'https://github.com/pauljoda/Prismedia/tree/main/documentation-site/',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    image: 'img/screenshots/dashboard.png',
    colorMode: {
      defaultMode: 'dark',
      disableSwitch: false,
      respectPrefersColorScheme: false,
    },
    navbar: {
      title: 'Prismedia',
      logo: {
        alt: 'Prismedia logo',
        src: 'img/logo.png',
      },
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'docsSidebar',
          position: 'left',
          label: 'Docs',
        },
        {to: '/docs/getting-started/install', label: 'Get Started', position: 'left'},
        {to: '/docs/jellyfin/overview', label: 'Jellyfin', position: 'left'},
        {to: '/docs/developers/architecture', label: 'Developers', position: 'left'},
        {to: '/docs/plugins/overview', label: 'Plugins', position: 'left'},
        {
          href: 'https://github.com/pauljoda/Prismedia',
          label: 'GitHub',
          position: 'right',
        },
        {
          href: 'https://www.reddit.com/r/Prismedia/',
          label: 'Reddit',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Docs',
          items: [
            {
              label: 'About Prismedia',
              to: '/docs/intro',
            },
            {
              label: 'Install & Run',
              to: '/docs/getting-started/install',
            },
          ],
        },
        {
          title: 'Build',
          items: [
            {
              label: 'Architecture',
              to: '/docs/developers/architecture',
            },
            {
              label: 'Plugin development',
              to: '/docs/plugins/overview',
            },
          ],
        },
        {
          title: 'Project',
          items: [
            {
              label: 'GitHub',
              href: 'https://github.com/pauljoda/Prismedia',
            },
            {
              label: 'Releases',
              href: 'https://github.com/pauljoda/Prismedia/releases',
            },
            {
              label: 'Subreddit',
              href: 'https://www.reddit.com/r/Prismedia/',
            },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} Prismedia. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismediaPrismTheme,
      additionalLanguages: ['bash', 'json', 'yaml', 'sql', 'python', 'css'],
    },
    mermaid: {
      theme: {
        light: 'neutral',
        dark: 'dark',
      },
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
