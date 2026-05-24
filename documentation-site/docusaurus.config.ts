import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

// This runs in Node.js - Don't use client-side code here (browser APIs, JSX...)

const config: Config = {
  title: 'Prismedia',
  tagline: 'A private, self-hosted home for your entire media collection.',
  favicon: 'img/logo.png',

  // Future flags, see https://docusaurus.io/docs/api/docusaurus-config#future
  future: {
    v4: true, // Improve compatibility with the upcoming Docusaurus v4
  },

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
        {to: '/docs/users/quick-start', label: 'Users', position: 'left'},
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
              label: 'Quick start',
              to: '/docs/users/quick-start',
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
      darkTheme: prismThemes.dracula,
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
