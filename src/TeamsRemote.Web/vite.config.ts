import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import fs from 'node:fs'
import path from 'node:path'

// Read central version from repository root version.json
let appVersion = '0.9.0-beta.3';
try {
  const versionFilePath = path.resolve(import.meta.dirname, '../../version.json');
  if (fs.existsSync(versionFilePath)) {
    const versionData = JSON.parse(fs.readFileSync(versionFilePath, 'utf-8'));
    appVersion = versionData.prerelease
      ? `${versionData.major}.${versionData.minor}.${versionData.patch}-${versionData.prerelease}`
      : `${versionData.major}.${versionData.minor}.${versionData.patch}`;
  }
} catch {
  // Use fallback
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  define: {
    __APP_VERSION__: JSON.stringify(appVersion),
  },
  server: {
    host: '0.0.0.0',
    port: 5173,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/test/setup.ts',
  },
})
