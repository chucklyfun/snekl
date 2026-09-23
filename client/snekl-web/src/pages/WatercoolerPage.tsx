import { type FormEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { createContentClient, hasPermission } from '../api';
import { EmoteAutocomplete } from '../components/EmoteAutocomplete';
import { EmotePicker } from '../components/EmotePicker';
import { NsfwAgeCheckPanel, forumAgeStatus } from '../components/NsfwAgeCheckPanel';
import { PostBody } from '../components/PostBody';
import { ReactionBar } from '../components/ReactionBar';
import { UpvoteButton } from '../components/UpvoteButton';
import { UserByline } from '../components/UserPresentation';
import { useAuth } from '../AuthContext';
import { loadContextEmotes } from '../emoteCatalog';
import { catalogEmoteShortcode, insertEmoteToken, type CatalogEmote } from '../emotes';
import { useServerRegistry } from '../ServerRegistryContext';
import { postOwnerBaseUrl } from '../postOwner';
import {
  decodeServerDid,
  forumPath,
  forumSettingsPath,
  resolveServerBaseUrl,
  threadPath,
  watercoolerSettingsPath,
} from '../serverDid';
import type { Forum, Post, Watercooler } from '../types';
import { isForumPending } from '../createForumAccess';
import { usePostRealtime } from '../usePostRealtime';

export function WatercoolerPage() {
  const { serverDid: encodedDid, slug, parentSlug, coolerSlug } = useParams<{
    serverDid: string;
    slug?: string;
    parentSlug?: string;
    coolerSlug?: string;
  }>();
  const [searchParams] = useSearchParams();
  const { user } = useAuth();
  const { resolveBaseUrl, activeServers } = useServerRegistry();
  const messageRef = useRef<HTMLTextAreaElement | null>(null);
  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);

  const isNestedForum = Boolean(parentSlug && slug && coolerSlug);
  const coolerName = coolerSlug ?? slug ?? '';
  const forumSlug = coolerSlug ? slug : undefined;
  const forumParentSlug = isNestedForum ? parentSlug : undefined;

  const [cooler, setCooler] = useState<Watercooler | null>(null);
  const [parentForum, setParentForum] = useState<Forum | null>(null);
  const [posts, setPosts] = useState<Post[]>([]);
  const [message, setMessage] = useState('');
  const [quoteId, setQuoteId] = useState(searchParams.get('quote') ?? '');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [repliesFollowingOnly, setRepliesFollowingOnly] = useState(false);

  const [ageBlocked, setAgeBlocked] = useState(false);
  const [emotes, setEmotes] = useState<CatalogEmote[]>([]);

  const load = useCallback(async () => {
    if (!client || !coolerName) return;
    const parentForumRow = forumSlug
      ? await client.getForum(forumSlug, forumParentSlug)
      : null;
    const row = await client.getWatercooler(coolerName, parentForumRow?.id);
    setParentForum(parentForumRow);
    setCooler(row);
    if (parentForumRow?.nsfwAgeCheckBlocksEntry) {
      setAgeBlocked(true);
      setPosts([]);
      return;
    }
    setAgeBlocked(false);
    if (!hasPermission(row.viewerPermissions, 'ViewWatercooler') && !row.viewerCanManage) {
      setPosts([]);
      return;
    }
    const sort = row.allowUpvotes ? 'hot' : 'new';
    setPosts(await client.getWatercoolerStream(row.id, sort, 1, 50));
  }, [client, coolerName, forumSlug, forumParentSlug]);

  useEffect(() => {
    void load().catch((err) => setError(err instanceof Error ? err.message : 'Failed to load'));
  }, [load]);

  useEffect(() => {
    if (!client) return;
    if (forumSlug && !parentForum?.id) return;
    void loadContextEmotes({
      homeClient: client,
      forumId: parentForum?.id,
      destPermissions: parentForum?.viewerPermissions,
      homeServerDid: serverDid,
      activeServers,
    })
      .then(setEmotes)
      .catch(() => setEmotes([]));
  }, [activeServers, client, forumSlug, parentForum?.id, parentForum?.viewerPermissions, serverDid]);

  usePostRealtime({
    baseUrl,
    forumId: null,
    threadId: null,
    watercoolerId: cooler?.id,
    onEvent: () => {
      void load().catch(() => undefined);
    },
  });

  const canSpeak = hasPermission(cooler?.viewerPermissions, 'SpeakWatercooler') && !isForumPending(cooler);
  const canView = hasPermission(cooler?.viewerPermissions, 'ViewWatercooler') || cooler?.viewerCanManage;
  const composerAtBottom = cooler?.newestAtBottom !== false;
  const emoteHome = { forumId: parentForum?.id, serverDid };
  const emoteSettingsHref = parentForum
    ? `${forumSettingsPath(serverDid, parentForum.slug, parentForum.parentSlug ?? forumParentSlug)}#emotes`
    : undefined;
  const insertMessageEmote = (token: string) => {
    const el = messageRef.current;
    const start = el?.selectionStart ?? message.length;
    const end = el?.selectionEnd ?? message.length;
    const { next, caret } = insertEmoteToken(message, start, end, token);
    setMessage(next);
    queueMicrotask(() => {
      el?.focus();
      el?.setSelectionRange(caret, caret);
    });
  };
  const canOpenSettings =
    Boolean(cooler?.viewerCanManage) ||
    Boolean(cooler?.viewerCanApprove) ||
    hasPermission(cooler?.viewerPermissions, 'ManageWatercooler') ||
    hasPermission(cooler?.viewerPermissions, 'ViewHistory');

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!client || !cooler || !message.trim()) return;
    setBusy(true);
    setError('');
    try {
      await client.createPost(
        undefined,
        message.trim(),
        undefined,
        undefined,
        undefined,
        cooler.id,
        quoteId || undefined,
        undefined,
        repliesFollowingOnly ? 'following' : undefined,
      );
      setMessage('');
      setQuoteId('');
      setRepliesFollowingOnly(false);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to post');
    } finally {
      setBusy(false);
    }
  };

  const composer =
    user && canSpeak && cooler && !ageBlocked ? (
      <form
        className={`card cooler-composer ${composerAtBottom ? 'cooler-composer-bottom' : 'cooler-composer-top'}`}
        onSubmit={submit}
      >
        {quoteId && (
          <p className="card-meta">
            Quoting a post.{' '}
            <button type="button" className="btn-ghost" onClick={() => setQuoteId('')}>
              Cancel quote
            </button>
          </p>
        )}
        <div className="form-group">
          <EmoteAutocomplete emotes={emotes} home={emoteHome} value={message} onChange={setMessage} disabled={busy}>
            {(bind) => (
              <textarea
                ref={(el) => {
                  bind.ref(el);
                  messageRef.current = el;
                }}
                value={message}
                onChange={bind.onChange}
                onKeyDown={bind.onKeyDown}
                onSelect={bind.onSelect}
                onClick={bind.onClick}
                onBlur={bind.onBlur}
                placeholder={
                  cooler.allowUpvotes
                    ? 'Post in the plaza… Type : for emotes'
                    : 'Message the channel… Type : for emotes'
                }
                required
              />
            )}
          </EmoteAutocomplete>
          <EmotePicker
            emotes={emotes}
            home={emoteHome}
            disabled={busy}
            manageHref={emoteSettingsHref}
            onPickEmote={(emote) =>
              insertMessageEmote(
                emote.imageUrl
                  ? catalogEmoteShortcode(emote, emotes, emoteHome)
                  : emote.skinsUnicode ?? catalogEmoteShortcode(emote, emotes, emoteHome),
              )
            }
          />
        </div>
        <label className="create-post-nsfw" style={{ marginBottom: '0.75rem' }}>
          <input
            type="checkbox"
            checked={repliesFollowingOnly}
            disabled={busy}
            onChange={(e) => setRepliesFollowingOnly(e.target.checked)}
          />
          Only people you follow can reply
        </label>
        <button type="submit" className="btn-primary" disabled={busy}>
          Send
        </button>
      </form>
    ) : null;

  return (
    <div>
      {cooler && (
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: '1rem' }}>
          <div>
            <h1 className="page-title">{cooler.name}</h1>
            <p className="page-subtitle">
              {cooler.parentForumSlug ? (
                <>
                  Watercooler in{' '}
                  <Link to={forumPath(serverDid, cooler.parentForumSlug, forumParentSlug)}>
                    {cooler.parentForumName ?? cooler.parentForumSlug}
                  </Link>
                </>
              ) : (
                'Server plaza'
              )}
              {cooler.description ? ` · ${cooler.description}` : ''}
            </p>
          </div>
          {user && canOpenSettings && (
            <div className="page-actions">
              <Link
                to={watercoolerSettingsPath(
                  serverDid,
                  cooler.slug,
                  cooler.parentForumSlug ?? undefined,
                  forumParentSlug,
                )}
                className="btn-ghost"
              >
                Settings
              </Link>
            </div>
          )}
        </div>
      )}
      {error && <div className="error-banner">{error}</div>}
      {client && parentForum && (
        <NsfwAgeCheckPanel
          client={client}
          forumId={parentForum.id}
          status={forumAgeStatus(parentForum)}
          loggedIn={Boolean(user)}
          onCompleted={() => {
            void load().catch(() => undefined);
          }}
        />
      )}
      {cooler && isForumPending(cooler) && <p className="card-meta">Waiting for approval.</p>}
      {cooler && !canView && !ageBlocked && (
        <p className="card-meta">You can see this room but cannot read messages yet.</p>
      )}
      {!composerAtBottom && composer}
      {canView && !ageBlocked && baseUrl && (
        <div className={cooler?.newestAtBottom ? 'cooler-channel' : undefined}>
          {posts.map((post) => (
            <article key={post.id} className="card cooler-post">
              {cooler?.allowUpvotes && (
                <UpvoteButton
                  postId={post.id}
                  baseUrl={postOwnerBaseUrl(post, baseUrl)}
                  upvoteCount={post.upvoteCount}
                  viewerHasUpvoted={post.viewerHasUpvoted}
                  author={post.author}
                  canUpvote={post.viewerActions?.canUpvote}
                  compact
                />
              )}
              <UserByline presentation={post.author} timestamp={post.createdAt} showStatus={false} />
              {post.parentPostId && (
                <div className="card-meta">
                  Replying to a post · <Link to={threadPath(serverDid, post.parentPostId)}>Open chain</Link>
                </div>
              )}
              {post.quotedPost && (
                <blockquote className="cooler-quote">
                  {post.quotedPost.viewerCanView ? (
                    <>
                      <UserByline presentation={post.quotedPost.author} timestamp={post.quotedPost.createdAt} showStatus={false} />
                      <PostBody message={post.quotedPost.message} emotes={emotes} home={emoteHome} />
                    </>
                  ) : (
                    <p className="card-meta">Quoted post is locked.</p>
                  )}
                </blockquote>
              )}
              <PostBody message={post.message} emotes={emotes} home={emoteHome} />
              <div className="post-actions">
                <Link to={threadPath(serverDid, post.id)} className="btn-ghost">
                  {post.replyCount} replies
                </Link>
                {canSpeak && (
                  <button type="button" className="btn-ghost" onClick={() => setQuoteId(post.id)}>
                    Quote
                  </button>
                )}
              </div>
              {baseUrl && (
                <ReactionBar
                  postId={post.id}
                  baseUrl={postOwnerBaseUrl(post, baseUrl)}
                  reactions={post.reactions ?? []}
                  emotes={emotes}
                  forumId={parentForum?.id}
                  manageHref={emoteSettingsHref}
                  disabled={post.isFederated}
                />
              )}
            </article>
          ))}
        </div>
      )}
      {composerAtBottom && composer}
    </div>
  );
}
