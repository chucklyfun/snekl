import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const rootDir = path.dirname(fileURLToPath(import.meta.url))

function copyAdminPlugin() {
  return {
    name: 'copy-admin-to-wwwroots',
    closeBundle() {
      const src = path.join(rootDir, 'dist-admin')
      if (!fs.existsSync(src)) return
      const dests = [
        path.join(rootDir, '../../src/Snekl.Api/wwwroot/admin'),
        path.join(rootDir, '../../src/Snekl.Hub/wwwroot/admin'),
        path.join(rootDir, '../../src/Snekl.Account.Api/wwwroot/admin'),
      ]
      for (const dest of dests) {
        fs.rmSync(dest, { recursive: true, force: true })
        fs.mkdirSync(dest, { recursive: true })
        fs.cpSync(src, dest, { recursive: true })
      }
    },
  }
}

export default defineConfig({
  plugins: [react(), copyAdminPlugin()],
  base: '/admin/',
  root: path.join(rootDir, 'admin'),
  publicDir: false,
  server: {
    fs: { allow: [rootDir] },
  },
  build: {
    outDir: path.join(rootDir, 'dist-admin'),
    emptyOutDir: true,
  },
})
