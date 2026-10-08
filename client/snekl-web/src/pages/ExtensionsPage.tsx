import { Navigate } from 'react-router';
import { useServerRegistry } from '../ServerRegistryContext';
import { serverSettingsPath } from '../serverDid';

/** Old top-level /extensions → server settings Extensions section. */
export function ExtensionsPage() {
  const { activeServers } = useServerRegistry();
  const origin = activeServers[0];
  if (origin) {
    return <Navigate to={`${serverSettingsPath(origin.serverDid)}#extensions`} replace />;
  }
  return <Navigate to="/servers" replace />;
}
