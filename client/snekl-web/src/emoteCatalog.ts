import { createContentClient, hasPermission } from './api';
import type { CatalogEmote } from './emotes';
import type { ContentServerMembership } from './types';

export async function loadContextEmotes(input: {
  homeClient: ReturnType<typeof createContentClient>;
  forumId?: string;
  destPermissions?: string[];
  homeServerDid?: string;
  activeServers?: ContentServerMembership[];
}): Promise<CatalogEmote[]> {
  const local = await input.homeClient.getEmotes(input.forumId).catch(() => [] as CatalogEmote[]);
  const byId = new Map(local.map((emote) => [emote.id, emote]));

  if (!hasPermission(input.destPermissions, 'UseOtherServerEmotes') || !input.activeServers?.length) {
    return [...byId.values()];
  }

  const insertOtherForums = hasPermission(input.destPermissions, 'UseOtherForumEmotes');
  const others = input.activeServers.filter((server) => server.serverDid !== input.homeServerDid);
  const foreign = await Promise.all(
    others.map(async (server) => {
      const client = createContentClient(server.baseUrl);
      const rows = await client.getEmotes(undefined, { allViewable: true }).catch(() => [] as CatalogEmote[]);
      return rows.map((emote) => ({
        ...emote,
        serverDid: emote.serverDid ?? server.serverDid,
        serverPrefix: emote.serverPrefix,
        canInsert: !emote.forumId || insertOtherForums,
      }));
    }),
  );

  for (const emote of foreign.flat()) {
    if (!byId.has(emote.id)) byId.set(emote.id, emote);
  }
  return [...byId.values()];
}
