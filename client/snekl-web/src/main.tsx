import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App.tsx'
import { loadRuntimeConfig } from './hosts.ts'
import { installNativeShell } from './nativeShell.ts'

installNativeShell()
await loadRuntimeConfig()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
