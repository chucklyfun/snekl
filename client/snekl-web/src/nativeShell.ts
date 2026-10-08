import { App } from '@capacitor/app';
import { Capacitor } from '@capacitor/core';

/** Hardware back follows in-app history, then leaves the app. */
export function installNativeShell(): void {
  if (!Capacitor.isNativePlatform()) return;

  void App.addListener('backButton', ({ canGoBack }) => {
    if (canGoBack) {
      window.history.back();
      return;
    }
    void App.exitApp();
  });
}
