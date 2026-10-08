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
      const hosts = [
        path.join(rootDir, '../../src/Snekl.Api/wwwroot'),
        path.join(rootDir, '../../src/Snekl.Hub/wwwroot'),
        path.join(rootDir, '../../src/Snekl.Account.Api/wwwroot'),
      ]
      for (const wwwroot of hosts) {
        fs.rmSync(path.join(wwwroot, 'admin'), { recursive: true, force: true })
        const dest = path.join(wwwroot, 'server')
        fs.rmSync(dest, { recursive: true, force: true })
        fs.mkdirSync(dest, { recursive: true })
        fs.cpSync(src, dest, { recursive: true })
      }
    },
  }
}

export default defineConfig({
  plugins: [react(), copyAdminPlugin()],
  base: '/server/',
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
