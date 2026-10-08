import { useEffect, useRef } from 'react';
import { useNavigate } from 'react-router';
import { Capacitor } from '@capacitor/core';
import { PushNotifications } from '@capacitor/push-notifications';
import { createContentClient } from './api';
import { notifyReplyAlertsChanged, replyAlertPath } from './replyAlerts';
import type { ReplyAlert, ReplyAlertKind } from './types';
import type { ContentServerMembership } from './types';

export function useReplyAlertPush(servers: ContentServerMembership[]): void {
  const navigate = useNavigate();
  const serversRef = useRef(servers);
  serversRef.current = servers;
  const navigateRef = useRef(navigate);
  navigateRef.current = navigate;
  const tokenRef = useRef<string | null>(null);
  const serverKey = servers.map((server) => server.serverDid).join('|');

  useEffect(() => {
    if (!Capacitor.isNativePlatform() || serversRef.current.length === 0) return;

    let cancelled = false;
    const handles: Array<{ remove: () => Promise<void> }> = [];

    const publish = async (token: string) => {
      await Promise.all(
        serversRef.current.map((server) =>
          createContentClient(server.baseUrl)
            .registerPushDevice({ platform: 'android', token })
            .catch(() => {}),
        ),
      );
    };

    void (async () => {
      const permission = await PushNotifications.requestPermissions();
      if (cancelled || permission.receive !== 'granted') return;

      handles.push(
        await PushNotifications.addListener('registration', (event) => {
          tokenRef.current = event.value;
          void publish(event.value);
        }),
      );
      handles.push(
        await PushNotifications.addListener('pushNotificationActionPerformed', (action) => {
          const data = action.notification.data ?? {};
          const serverDid = stringField(data, 'serverDid');
          const rootPostId = stringField(data, 'rootPostId');
          const alertId = stringField(data, 'alertId');
          const server = serversRef.current.find((item) => item.serverDid === serverDid);
          if (server && alertId) {
            void createContentClient(server.baseUrl)
              .markReplyAlertRead(alertId)
              .then(() => notifyReplyAlertsChanged())
              .catch(() => {});
          }
          if (!serverDid) return;
          const kind = stringField(data, 'kind');
          const alert: ReplyAlert = {
            id: alertId,
            rootPostId: rootPostId || null,
            title: stringField(data, 'title'),
            replyCount: Number(stringField(data, 'replyCount')) || 1,
            latestReplyId: '',
            latestReplyAt: '',
            createdAt: '',
            readAt: null,
            kind: isReplyAlertKind(kind) ? kind : 'thread',
            slug: stringField(data, 'slug') || null,
            parentSlug: stringField(data, 'parentSlug') || null,
            forumParentSlug: stringField(data, 'forumParentSlug') || null,
          };
          const path = replyAlertPath(serverDid, alert);
          if (path !== '/alerts') navigateRef.current(path);
        }),
      );

      await PushNotifications.register();
      if (tokenRef.current) await publish(tokenRef.current);
    })().catch(() => {});

    return () => {
      cancelled = true;
      for (const handle of handles) void handle.remove();
    };
  }, [serverKey]);
}

function isReplyAlertKind(value: string): value is ReplyAlertKind {
  return value === 'thread' || value === 'nest' || value === 'watercooler' || value === 'vote' || value === 'follow';
}

function stringField(data: object, key: string): string {
  if (!(key in data)) return '';
  const value = (data as Record<string, unknown>)[key];
  return typeof value === 'string' ? value : '';
}
