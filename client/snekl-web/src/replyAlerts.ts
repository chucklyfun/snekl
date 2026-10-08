import { forumPath, threadPath, watercoolerPath } from './serverDid';
import type { ContentServerMembership, ReplyAlert, ReplyAlertKind, ReplyAlertMode } from './types';

export interface MergedReplyAlert extends ReplyAlert {
  serverDid: string;
  serverName: string;
  baseUrl: string;
}

export interface ReplyAlertServerFailure {
  serverDid: string;
  serverName: string;
}

export function replyAlertSummary(count: number, title: string, kind: ReplyAlertKind = 'thread'): string {
  switch (kind) {
    case 'thread':
      return count <= 1 ? `New reply on ${title}` : `${count} new replies on ${title}`;
    case 'nest':
    case 'watercooler':
      return count <= 1 ? `New post in ${title}` : `${count} new posts in ${title}`;
    case 'vote':
      return `${count} votes on ${title}`;
    case 'follow':
      return count <= 1 ? `New post from ${title}` : `${count} new posts from ${title}`;
    default: {
      const exhaustive: never = kind;
      return exhaustive;
    }
  }
}

export function replyAlertPath(serverDid: string, alert: ReplyAlert): string {
  const kind = alert.kind ?? 'thread';
  if (kind === 'vote') {
    if (alert.rootPostId) return threadPath(serverDid, alert.rootPostId);
    return '/alerts';
  }
  if (kind === 'follow') {
    if (alert.replyCount <= 1 && alert.rootPostId) return threadPath(serverDid, alert.rootPostId);
    if (alert.slug) return `/users/${encodeURIComponent(alert.slug)}`;
    return '/alerts';
  }
  const singleThread = alert.replyCount <= 1 && alert.rootPostId;
  if (kind === 'thread' || singleThread) {
    if (alert.rootPostId) return threadPath(serverDid, alert.rootPostId);
  }
  if (kind === 'nest' && alert.slug) return forumPath(serverDid, alert.slug, alert.parentSlug ?? undefined);
  if (kind === 'watercooler' && alert.slug) {
    return watercoolerPath(serverDid, alert.slug, alert.parentSlug ?? undefined, alert.forumParentSlug ?? undefined);
  }
  if (alert.rootPostId) return threadPath(serverDid, alert.rootPostId);
  return '/alerts';
}

export function parseReplyAlertMode(value: string): ReplyAlertMode | null {
  switch (value) {
    case 'replies':
    case 'all':
    case 'off':
      return value;
    default:
      return null;
  }
}

export function replyAlertModeLabel(mode: ReplyAlertMode): string {
  switch (mode) {
    case 'replies':
      return 'Replies';
    case 'all':
      return 'All activity';
    case 'off':
      return 'Off';
    default: {
      const exhaustive: never = mode;
      return exhaustive;
    }
  }
}

export function mergeReplyAlerts(
  groups: Array<{ server: ContentServerMembership; alerts: ReplyAlert[] }>,
): MergedReplyAlert[] {
  return groups
    .flatMap(({ server, alerts }) =>
      alerts.map((alert) => ({
        ...alert,
        serverDid: server.serverDid,
        serverName: server.name,
        baseUrl: server.baseUrl,
      })),
    )
    .sort((a, b) => Date.parse(b.latestReplyAt) - Date.parse(a.latestReplyAt));
}

export const replyAlertsChangedEvent = 'snekl-reply-alerts-changed';

export function notifyReplyAlertsChanged(): void {
  window.dispatchEvent(new Event(replyAlertsChangedEvent));
}
