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
  tagline: 'Your whole media life. One private home.',
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
    image: 'img/showcase/prism-refraction-hero.png',
    metadata: [
      {
        name: 'keywords',
        content:
          'self-hosted media library, private media server, movies, music, audiobooks, ebooks, comics, iOS, Apple TV, Docker',
      },
      {name: 'application-name', content: 'Prismedia'},
      {name: 'apple-mobile-web-app-title', content: 'Prismedia'},
      {name: 'theme-color', content: '#050506'},
      {property: 'og:type', content: 'website'},
      {property: 'og:site_name', content: 'Prismedia'},
      {
        name: 'twitter:title',
        content: 'Your whole media life. One private home.',
      },
      {
        name: 'twitter:description',
        content:
          'A private, self-hosted media library for web, iPhone, iPad, and Apple TV.',
      },
    ],
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
        {to: '/?section=product', label: 'Product', position: 'left'},
        {to: '/?section=experiences', label: 'Experiences', position: 'left'},
        {to: '/?section=platforms', label: 'Platforms', position: 'left'},
        {to: '/?section=self-hosting', label: 'Self-hosting', position: 'left'},
        {
          type: 'docSidebar',
          sidebarId: 'docsSidebar',
          position: 'left',
          label: 'Docs',
        },
        {
          href: 'https://github.com/pauljoda/Prismedia',
          label: 'GitHub',
          position: 'right',
        },
        {
          href: 'https://testflight.apple.com/join/c9bgDxr7',
          label: 'Join TestFlight',
          position: 'right',
          className: 'navbar__testflight',
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
          title: 'Product',
          items: [
            {
              label: 'Experiences',
              to: '/?section=experiences',
            },
            {
              label: 'Platforms',
              to: '/?section=platforms',
            },
            {
              label: 'Self-hosting',
              to: '/?section=self-hosting',
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
            {
              label: 'Join TestFlight',
              href: 'https://testflight.apple.com/join/c9bgDxr7',
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
