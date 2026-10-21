import {defineConfig} from '@rsbuild/core';
import {pluginReact} from '@rsbuild/plugin-react';

export default defineConfig({
  plugins: [pluginReact()],
  source: {entry: {index: './src/main.tsx'}},
  html: {
    title: 'TimeGolf',
    favicon: './public/favicon.svg',
    tags: [
      {tag: 'link', attrs: {rel: 'preconnect', href: 'https://fonts.googleapis.com'}},
      {tag: 'link', attrs: {rel: 'preconnect', href: 'https://fonts.gstatic.com', crossorigin: ''}},
      {
        tag: 'link',
        attrs: {
          rel: 'stylesheet',
          href: 'https://fonts.googleapis.com/css2?family=Lilita+One&family=Nunito:wght@600;700;800;900&display=swap'
        }
      }
    ]
  },
  server: {
    port: 3000,
    proxy: {
      '/hubs': {target: 'http://localhost:5000', ws: true},
      '/health': 'http://localhost:5000'
    }
  }
});
