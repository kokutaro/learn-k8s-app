import tailwindcss from '@tailwindcss/vite';
import { tanstackRouter } from '@tanstack/router-plugin/vite';
import react from '@vitejs/plugin-react';
import { loadEnv } from 'vite';
import { defineConfig } from 'vitest/config';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const apiTarget = env.OSOUJISYSTEM_WEBAPI_HTTPS
  const useProxy = Boolean(apiTarget)

  return {
    plugins: [tanstackRouter(), react(), tailwindcss()],
    server: useProxy
      ? {
        proxy: {
          '/api': {
            target: apiTarget,
            changeOrigin: true,
            secure: false,
          },
          '/openapi': {
            target: apiTarget,
            changeOrigin: true,
            secure: false,
          },
          '/health': {
            target: apiTarget,
            changeOrigin: true,
            secure: false,
          },
        },
      }
      : undefined,
    test: {
      environment: 'jsdom',
      setupFiles: './src/test/setup.ts',
      globals: true,
      exclude: ['tests/e2e/**', 'node_modules/**', 'src/routes/**'],
    },
  }
});
