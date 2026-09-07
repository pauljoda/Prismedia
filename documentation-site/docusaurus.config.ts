import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';
import type {PrismTheme} from 'prism-react-renderer';
import {SITE_ORIGIN, SITE_BASE_URL, SOCIAL_IMAGE_PATH, SOCIAL_IMAGE_ALT} from './site-metadata';

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

// Landing-page section links share the root pathname, so Docusaurus' path-only
// active-state matching would otherwise mark every section as selected.
const disablePathOnlyActiveState = 'a^';

// This runs in Node.js - Don't use client-side code here (browser APIs, JSX...)

const config: Config = {
  title: 'Prismedia',
  tagline: 'A clear home for all your media.',
  favicon: 'img/icons/favicon.ico',
  headTags: [
    {tagName: 'link', attributes: {rel: 'manifest', href: `${SITE_BASE_URL}site.webmanifest`}},
    {tagName: 'link', attributes: {rel: 'icon', type: 'image/png', sizes: '96x96', href: `${SITE_BASE_URL}img/icons/favicon-96.png`}},
    {tagName: 'link', attributes: {rel: 'apple-touch-icon', sizes: '180x180', href: `${SITE_BASE_URL}img/icons/apple-touch-icon.png`}},
  ],

  // Future flags, see https://docusaurus.io/docs/api/docusaurus-config#future
  future: {
    v4: true, // Improve compatibility with the upcoming Docusaurus v4
  },
  markdown: {
    mermaid: true,
  },
  themes: [
    '@docusaurus/theme-mermaid',
    [
      '@easyops-cn/docusaurus-search-local',
      {
        hashed: 'filename',
        indexBlog: false,
        indexPages: false,
        highlightSearchTermsOnTargetPage: true,
        searchBarPosition: 'right',
      },
    ],
  ],

  // Set the production url of your site here
  url: SITE_ORIGIN,
  // Set the /<baseUrl>/ pathname under which your site is served
  // For GitHub pages deployment, it is often '/<projectName>/'
  baseUrl: SITE_BASE_URL,

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
        sitemap: {
          ignorePatterns: ['**/search'],
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    image: SOCIAL_IMAGE_PATH,
    metadata: [
      {name: 'application-name', content: 'Prismedia'},
      {name: 'apple-mobile-web-app-title', content: 'Prismedia'},
      {name: 'theme-color', content: '#050609'},
      {name: 'color-scheme', content: 'dark'},
      {name: 'robots', content: 'index, follow, max-image-preview:large'},
      {property: 'og:type', content: 'website'},
      {property: 'og:site_name', content: 'Prismedia'},
      {property: 'og:image:width', content: '1200'},
      {property: 'og:image:height', content: '630'},
      {property: 'og:image:type', content: 'image/png'},
      {property: 'og:image:alt', content: SOCIAL_IMAGE_ALT},
      // Docusaurus supplies each page's Open Graph title and description, which
      // social cards also use. Global Twitter overrides would flatten every doc.
      {name: 'twitter:image:alt', content: SOCIAL_IMAGE_ALT},
    ],
    colorMode: {
      defaultMode: 'dark',
      disableSwitch: true,
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
          to: '/?section=workflow',
          label: 'How it works',
          position: 'left',
          activeBaseRegex: disablePathOnlyActiveState,
          className: 'navbar__section-link',
        },
        {
          to: '/?section=experiences',
          label: 'Experiences',
          position: 'left',
          activeBaseRegex: disablePathOnlyActiveState,
          className: 'navbar__section-link',
        },
        {
          to: '/?section=platforms',
          label: 'Platforms',
          position: 'left',
          activeBaseRegex: disablePathOnlyActiveState,
          className: 'navbar__section-link',
        },
        {
          to: '/?section=self-hosting',
          label: 'Self-hosting',
          position: 'left',
          activeBaseRegex: disablePathOnlyActiveState,
          className: 'navbar__section-link',
        },
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
          className: 'navbar__github',
        },
        {
          href: 'https://testflight.apple.com/join/c9bgDxr7',
          label: 'TestFlight',
          position: 'right',
          className: 'navbar__testflight marketing-glass',
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
              label: 'Start Here',
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
              label: 'Test early builds',
              href: 'https://testflight.apple.com/join/c9bgDxr7',
            },
            {
              label: 'Support',
              to: '/support',
            },
            {
              label: 'Privacy',
              to: '/privacy',
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
