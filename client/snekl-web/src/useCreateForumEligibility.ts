import { useEffect, useState } from 'react';
import { useAuth } from './AuthContext';
import {
  loadServerEligibility,
  membershipForDid,
  sortEligibleServers,
  type EligibleServer,
} from './createForumEligibility';
import { useServerRegistry } from './ServerRegistryContext';

export function useCreateForumEligibility(urlServerDid?: string) {
  const { user } = useAuth();
  const { activeServers, servers, resolveBaseUrl, loading: registryLoading } = useServerRegistry();
  const [eligibleServers, setEligibleServers] = useState<EligibleServer[]>([]);
  const [urlServerCanCreate, setUrlServerCanCreate] = useState<boolean | null>(null);
  const [urlServerCanRequest, setUrlServerCanRequest] = useState<boolean | null>(null);
  const [permissionsLoading, setPermissionsLoading] = useState(true);
  const [checkErrors, setCheckErrors] = useState<string[]>([]);

  useEffect(() => {
    if (!user) {
      setEligibleServers([]);
      setUrlServerCanCreate(null);
      setUrlServerCanRequest(null);
      setCheckErrors([]);
      setPermissionsLoading(false);
      return;
    }

    if (registryLoading) {
      setPermissionsLoading(true);
      return;
    }

    let cancelled = false;
    setPermissionsLoading(true);

    const run = async () => {
      if (urlServerDid) {
        const membership = membershipForDid(urlServerDid, servers, activeServers, resolveBaseUrl);
        if (!membership) {
          if (!cancelled) {
            setUrlServerCanCreate(false);
            setUrlServerCanRequest(false);
            setEligibleServers([]);
            setPermissionsLoading(false);
          }
          return;
        }
        const result = await loadServerEligibility(membership);
        if (cancelled) return;
        setUrlServerCanCreate(result.canCreateTopLevel);
        setUrlServerCanRequest(result.canRequestTopLevel);
        setEligibleServers([result]);
        setCheckErrors(result.checkError ? [`${result.name}: ${result.checkError}`] : []);
        setPermissionsLoading(false);
        return;
      }

      const candidates = activeServers.length > 0 ? activeServers : servers;
      const results = await Promise.all(candidates.map((server) => loadServerEligibility(server)));
      if (cancelled) return;
      const forPicker = sortEligibleServers(
        results.filter((s) => s.canCreateTopLevel || s.canRequestTopLevel),
      );
      setEligibleServers(forPicker);
      setCheckErrors(results.filter((s) => s.checkError).map((s) => `${s.name}: ${s.checkError}`));
      setUrlServerCanCreate(null);
      setUrlServerCanRequest(null);
      setPermissionsLoading(false);
    };

    void run();
    return () => {
      cancelled = true;
    };
  }, [user, activeServers, servers, urlServerDid, resolveBaseUrl, registryLoading]);

  const needsServerPicker = !urlServerDid;
  const canShowCreateButton =
    Boolean(user) && !permissionsLoading && eligibleServers.some((s) => s.canCreateTopLevel || s.canRequestTopLevel);

  return {
    eligibleServers,
    checkErrors,
    permissionsLoading,
    urlServerCanCreate,
    urlServerCanRequest,
    needsServerPicker,
    canShowCreateButton,
  };
}
