import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

export default defineConfig({
  site: 'https://dsantoreis.github.io/dotnetflow',
  base: '/dotnetflow',
  integrations: [
    starlight({
      title: 'dotnetflow',
      disable404Route: true,
      social: { github: 'https://github.com/dsantoreis/dotnetflow' },
      sidebar: [
        { label: 'Getting Started', slug: 'getting-started' },
        { label: 'Architecture', slug: 'architecture' },
        { label: 'API Reference', slug: 'api-reference' },
        { label: 'Deployment', slug: 'deployment' },
      ],
    }),
  ],
});
