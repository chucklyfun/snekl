import type { CapacitorConfig } from '@capacitor/cli';

const config: CapacitorConfig = {
  appId: 'dev.snekl.app',
  appName: 'Snekl',
  webDir: 'dist',
  backgroundColor: '#0f1419',
  android: {
    // The shell is https://localhost. Members can point Auth at an HTTP
    // server on the LAN, so the WebView must be allowed to call it.
    allowMixedContent: true,
  },
  server: {
    androidScheme: 'https',
    cleartext: true,
  },
};

export default config;
