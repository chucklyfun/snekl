import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { useEffect, useRef } from 'react';

export interface RealtimePostEvent {
  type: string;
  threadId: string;
  forumId?: string | null;
  watercoolerId?: string | null;
  postId: string;
  parentPostId?: string | null;
  upvoteCount?: number | null;
  isLocked?: boolean | null;
  isPinned?: boolean | null;
  isDeleted?: boolean | null;
}

interface UsePostRealtimeOptions {
  baseUrl: string | null | undefined;
  threadId?: string | null;
  forumId?: string | null;
  watercoolerId?: string | null;
  onEvent: (event: RealtimePostEvent) => void;
}

/**
 * Subscribes to live post events for a thread and/or forum via SignalR.
 * Relies on the content API hub at `/hubs/thread`.
 */
export function usePostRealtime({ baseUrl, threadId, forumId, watercoolerId, onEvent }: UsePostRealtimeOptions) {
  const onEventRef = useRef(onEvent);
  onEventRef.current = onEvent;

  useEffect(() => {
    if (!baseUrl || (!threadId && !forumId && !watercoolerId)) return;

    let cancelled = false;
    let connection: HubConnection | null = null;
    let debounceTimer: ReturnType<typeof setTimeout> | null = null;

    const hubUrl = `${baseUrl.replace(/\/$/, '')}/hubs/thread`;
    connection = new HubConnectionBuilder()
      // Hub is anonymous; skip cookies so CORS does not require credentials.
      .withUrl(hubUrl, { withCredentials: false })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('postEvent', (raw: RealtimePostEvent) => {
      if (cancelled) return;
      if (debounceTimer) clearTimeout(debounceTimer);
      debounceTimer = setTimeout(() => {
        onEventRef.current(raw);
      }, 150);
    });

    const start = async () => {
      try {
        await connection!.start();
        if (cancelled) return;
        if (threadId) await connection!.invoke('JoinThread', threadId);
        if (forumId) await connection!.invoke('JoinForum', forumId);
        if (watercoolerId) await connection!.invoke('JoinWatercooler', watercoolerId);
      } catch {
        // Live updates are best-effort; page still works via REST.
      }
    };

    void start();

    return () => {
      cancelled = true;
      if (debounceTimer) clearTimeout(debounceTimer);
      const conn = connection;
      connection = null;
      if (!conn) return;
      void (async () => {
        try {
          if (conn.state === HubConnectionState.Connected) {
            if (threadId) await conn.invoke('LeaveThread', threadId);
            if (forumId) await conn.invoke('LeaveForum', forumId);
            if (watercoolerId) await conn.invoke('LeaveWatercooler', watercoolerId);
          }
        } catch {
          /* ignore */
        }
        try {
          await conn.stop();
        } catch {
          /* ignore */
        }
      })();
    };
  }, [baseUrl, threadId, forumId, watercoolerId]);
}
