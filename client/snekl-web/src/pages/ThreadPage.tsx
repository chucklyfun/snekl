import { type FormEvent, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useParams } from 'react-router';
import { createContentClient } from '../api';
import { loadContextEmotes } from '../emoteCatalog';
import { EmoteAutocomplete } from '../components/EmoteAutocomplete';
import { EmotePicker } from '../components/EmotePicker';
import { NsfwAgeCheckPanel } from '../components/NsfwAgeCheckPanel';
import { PostAttachments } from '../components/PostAttachments';
import { PostBody } from '../components/PostBody';
import { ReactionBar } from '../components/ReactionBar';
import { UpvoteButton } from '../components/UpvoteButton';
import { UserByline } from '../components/UserPresentation';
import { useAuth } from '../AuthContext';
import { insertEmoteToken, catalogEmoteShortcode, type CatalogEmote, type EmoteHome } from '../emotes';
import { useForumNav } from '../ForumNavContext';
import { useServerRegistry } from '../ServerRegistryContext';
import { postOwnerBaseUrl } from '../postOwner';
import { decodeServerDid, forumSettingsPath, resolveServerBaseUrl, serverSettingsPath } from '../serverDid';
import type { NsfwAgeCheckStatus, Post, PostRevision } from '../types';
import { usePostRealtime } from '../usePostRealtime';

const isDev = import.meta.env.DEV;

function insertIntoField(
  value: string,
  setter: (next: string) => void,
  el: HTMLTextAreaElement | null,
  token: string,
) {
  const start = el?.selectionStart ?? value.length;
  const end = el?.selectionEnd ?? value.length;
  const { next, caret } = insertEmoteToken(value, start, end, token);
  setter(next);
  queueMicrotask(() => {
    el?.focus();
    el?.setSelectionRange(caret, caret);
  });
}

function PostNode({
  post,
  forumId,
  baseUrl,
  threadLocked,
  onChanged,
  createPost,
  updatePost,
  deletePost,
  getPostRevisions,
  setThreadLocked,
  setThreadPinned,
  setReplyPolicy,
  reportPost,
  emotes,
  home,
  manageHref,
}: {
  post: Post;
  forumId?: string | null;
  baseUrl: string;
  threadLocked: boolean;
  onChanged: () => void;
  createPost: (
    forumId: string | undefined,
    message: string,
    parentPostId?: string,
    title?: string,
    mediaAssetIds?: string[],
    watercoolerId?: string,
  ) => Promise<Post>;
  updatePost: (postId: string, message: string) => Promise<Post>;
  deletePost: (postId: string, hard?: boolean) => Promise<void>;
  getPostRevisions: (postId: string) => Promise<PostRevision[]>;
  setThreadLocked: (postId: string, locked: boolean) => Promise<void>;
  setThreadPinned: (postId: string, pinned: boolean) => Promise<void>;
  setReplyPolicy: (postId: string, replyPolicy: string) => Promise<void>;
  reportPost: (postId: string, reason: string) => Promise<void>;
  emotes: CatalogEmote[];
  home?: EmoteHome;
  manageHref?: string;
}) {
  const { user } = useAuth();
  const [reply, setReply] = useState('');
  const [editMessage, setEditMessage] = useState(post.message);
  const replyRef = useRef<HTMLTextAreaElement>(null);
  const editRef = useRef<HTMLTextAreaElement>(null);
  const [showReply, setShowReply] = useState(false);
  const [showEdit, setShowEdit] = useState(false);
  const [showHistory, setShowHistory] = useState(false);
  const [showReport, setShowReport] = useState(false);
  const [reportReason, setReportReason] = useState('');
  const [history, setHistory] = useState<PostRevision[]>([]);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    setEditMessage(post.isDeleted ? '' : post.message);
  }, [post.id, post.message, post.isDeleted]);

  const actions = post.viewerActions;
  const isRoot = !post.parentPostId;
  const canModerateLocked = Boolean(actions?.canLock || actions?.canUnlock);
  const canReply = actions?.canReply !== false;
  const repliesBlocked = (threadLocked && !canModerateLocked) || (Boolean(user) && !canReply);
  const followingOnly = post.replyPolicy === 'following';

  const submitReply = async (e: FormEvent) => {
    e.preventDefault();
    if (!reply.trim()) return;
    setError('');
    setBusy(true);
    try {
      await createPost(post.forumId ?? undefined, reply.trim(), post.id, undefined, undefined, post.watercoolerId ?? undefined);
      setReply('');
      setShowReply(false);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Reply failed');
    } finally {
      setBusy(false);
    }
  };

  const submitEdit = async (e: FormEvent) => {
    e.preventDefault();
    if (!editMessage.trim()) return;
    setError('');
    setBusy(true);
    try {
      await updatePost(post.id, editMessage.trim());
      setShowEdit(false);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Edit failed');
    } finally {
      setBusy(false);
    }
  };

  const softDelete = async () => {
    if (!window.confirm('Delete this post? The content will be hidden but history may remain for moderators.')) return;
    setError('');
    setBusy(true);
    try {
      await deletePost(post.id, false);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed');
    } finally {
      setBusy(false);
    }
  };

  const hardDelete = async () => {
    if (!window.confirm('Permanently remove this post and its history? This cannot be undone.')) return;
    setError('');
    setBusy(true);
    try {
      await deletePost(post.id, true);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Hard delete failed');
    } finally {
      setBusy(false);
    }
  };

  const loadHistory = async () => {
    if (showHistory) {
      setShowHistory(false);
      return;
    }
    setError('');
    try {
      const rows = await getPostRevisions(post.id);
      setHistory(rows);
      setShowHistory(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load history');
    }
  };

  const toggleLock = async (locked: boolean) => {
    setError('');
    setBusy(true);
    try {
      await setThreadLocked(post.id, locked);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Lock failed');
    } finally {
      setBusy(false);
    }
  };

  const togglePin = async (pinned: boolean) => {
    setError('');
    setBusy(true);
    try {
      await setThreadPinned(post.id, pinned);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Pin failed');
    } finally {
      setBusy(false);
    }
  };

  const toggleReplyPolicy = async (policy: 'everyone' | 'following') => {
    setError('');
    setBusy(true);
    try {
      await setReplyPolicy(post.id, policy);
      onChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not update reply settings');
    } finally {
      setBusy(false);
    }
  };

  const submitReport = async (e: FormEvent) => {
    e.preventDefault();
    if (!reportReason.trim()) return;
    setError('');
    setBusy(true);
    try {
      await reportPost(post.id, reportReason.trim());
      setReportReason('');
      setShowReport(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Report failed');
    } finally {
      setBusy(false);
    }
  };

  const classes = [
    'post-item',
    isRoot ? 'post-item-op' : '',
    post.isDeleted ? 'post-item-deleted' : '',
  ]
    .filter(Boolean)
    .join(' ');

  return (
    <div className={classes}>
      <div className="post-item-header">
        <UpvoteButton
          postId={post.id}
          baseUrl={postOwnerBaseUrl(post, baseUrl)}
          upvoteCount={post.upvoteCount}
          viewerHasUpvoted={post.viewerHasUpvoted}
          author={post.author}
          canUpvote={post.viewerActions?.canUpvote}
          compact={!isRoot}
        />
        <div className="post-item-content">
          {isRoot && (
            <div className="thread-badges">
              <span className="thread-badge thread-badge-op">Original post</span>
              {post.isPinned && <span className="thread-badge thread-badge-pinned">Pinned</span>}
              {post.isLocked && <span className="thread-badge thread-badge-locked">Locked</span>}
              {followingOnly && <span className="thread-badge thread-badge-following">Following only</span>}
            </div>
          )}
          {isRoot && !post.watercoolerId && (
            <h1 className="thread-root-title">{post.title?.trim() || 'Thread'}</h1>
          )}
          <UserByline presentation={post.author} timestamp={post.createdAt} editedAt={post.editedAt} />
          {isRoot && threadLocked && (
            <p className="card-meta">This thread is locked. New replies are disabled.</p>
          )}
          {isRoot && !threadLocked && followingOnly && (
            <p className="card-meta">
              {canReply
                ? 'Replies are limited to people the original poster follows.'
                : 'The original poster limited replies to people they follow.'}
            </p>
          )}
          {showEdit ? (
            <form onSubmit={submitEdit}>
              {error && <div className="error-banner">{error}</div>}
              <div className="form-group">
                <EmoteAutocomplete emotes={emotes} home={home} value={editMessage} onChange={setEditMessage} disabled={busy}>
                  {(bind) => (
                    <textarea
                      ref={(el) => {
                        bind.ref(el);
                        editRef.current = el;
                      }}
                      value={editMessage}
                      onChange={bind.onChange}
                      onKeyDown={bind.onKeyDown}
                      onSelect={bind.onSelect}
                      onClick={bind.onClick}
                      onBlur={bind.onBlur}
                      required
                    />
                  )}
                </EmoteAutocomplete>
                <EmotePicker
                  emotes={emotes}
                  home={home}
                  disabled={busy}
                  manageHref={manageHref}
                  onPickEmote={(emote) =>
                    insertIntoField(
                      editMessage,
                      setEditMessage,
                      editRef.current,
                      emote.imageUrl
                        ? catalogEmoteShortcode(emote, emotes, home)
                        : emote.skinsUnicode ?? catalogEmoteShortcode(emote, emotes, home),
                    )
                  }
                />
              </div>
              <div className="post-actions">
                <button type="submit" className="btn-primary" disabled={busy}>
                  Save edit
                </button>
                <button type="button" className="btn-ghost" onClick={() => setShowEdit(false)} disabled={busy}>
                  Cancel
                </button>
              </div>
            </form>
          ) : (
            <>
              <PostBody message={post.message} emotes={emotes} home={home} />
              {post.quotedPost && (
                <blockquote className="cooler-quote">
                  {post.quotedPost.viewerCanView ? (
                    <PostBody message={post.quotedPost.message} emotes={emotes} home={home} />
                  ) : (
                    <p className="card-meta">Quoted post is locked.</p>
                  )}
                </blockquote>
              )}
            </>
          )}
          {post.attachments && post.attachments.length > 0 && (
            <PostAttachments post={post} baseUrl={baseUrl} onChanged={onChanged} />
          )}
          {!post.isDeleted && (
            <ReactionBar
              postId={post.id}
              baseUrl={baseUrl}
              reactions={post.reactions ?? []}
              emotes={emotes}
              forumId={home?.forumId}
              disabled={post.isFederated}
              manageHref={manageHref}
              onChange={() => onChanged()}
            />
          )}
          {error && !showEdit && !showReport && <div className="error-banner">{error}</div>}
          <div className="post-actions">
            {user && !post.isDeleted && !repliesBlocked && (
              <button type="button" className="btn-ghost" onClick={() => setShowReply(!showReply)} disabled={busy}>
                Reply
              </button>
            )}
            {actions?.canEdit && !showEdit && (
              <button type="button" className="btn-ghost" onClick={() => setShowEdit(true)} disabled={busy}>
                Edit
              </button>
            )}
            {actions?.canSoftDelete && (
              <button type="button" className="btn-ghost" onClick={softDelete} disabled={busy}>
                Delete
              </button>
            )}
            {isDev && actions?.canHardDelete && (
              <button type="button" className="btn-ghost btn-danger" onClick={hardDelete} disabled={busy}>
                Purge (dev)
              </button>
            )}
            {actions?.canViewHistory && (
              <button type="button" className="btn-ghost" onClick={loadHistory} disabled={busy}>
                {showHistory ? 'Hide history' : 'Show history'}
              </button>
            )}
            {actions?.canLock && (
              <button type="button" className="btn-ghost" onClick={() => toggleLock(true)} disabled={busy}>
                Lock thread
              </button>
            )}
            {actions?.canUnlock && (
              <button type="button" className="btn-ghost" onClick={() => toggleLock(false)} disabled={busy}>
                Unlock thread
              </button>
            )}
            {actions?.canPin && (
              <button type="button" className="btn-ghost" onClick={() => togglePin(true)} disabled={busy}>
                Pin thread
              </button>
            )}
            {actions?.canUnpin && (
              <button type="button" className="btn-ghost" onClick={() => togglePin(false)} disabled={busy}>
                Unpin thread
              </button>
            )}
            {actions?.canSetReplyPolicy && followingOnly && (
              <button type="button" className="btn-ghost" onClick={() => toggleReplyPolicy('everyone')} disabled={busy}>
                Allow anyone to reply
              </button>
            )}
            {actions?.canSetReplyPolicy && !followingOnly && (
              <button type="button" className="btn-ghost" onClick={() => toggleReplyPolicy('following')} disabled={busy}>
                Limit replies to following
              </button>
            )}
            {actions?.canReport && (
              <button type="button" className="btn-ghost" onClick={() => setShowReport(!showReport)} disabled={busy}>
                Report
              </button>
            )}
          </div>
          {showReport && (
            <form onSubmit={submitReport} style={{ marginTop: '0.75rem' }}>
              <div className="form-group">
                <textarea
                  value={reportReason}
                  onChange={(e) => setReportReason(e.target.value)}
                  placeholder="Why are you reporting this post?"
                  required
                />
              </div>
              <div className="post-actions">
                <button type="submit" className="btn-primary" disabled={busy}>
                  Submit report
                </button>
                <button type="button" className="btn-ghost" onClick={() => setShowReport(false)} disabled={busy}>
                  Cancel
                </button>
              </div>
            </form>
          )}
          {showHistory && history.length > 0 && (
            <div className="post-history">
              {history.map((revision) => (
                <div key={revision.id} className="post-history-item">
                  <div className="card-meta">
                    {revision.editedByUsername} · {new Date(revision.createdAt).toLocaleString()}
                  </div>
                  <p className="post-body">{revision.message}</p>
                </div>
              ))}
            </div>
          )}
          {showReply && !post.isDeleted && !repliesBlocked && (
            <form onSubmit={submitReply}>
              <div className="form-group">
                <EmoteAutocomplete emotes={emotes} home={home} value={reply} onChange={setReply} disabled={busy}>
                  {(bind) => (
                    <textarea
                      ref={(el) => {
                        bind.ref(el);
                        replyRef.current = el;
                      }}
                      value={reply}
                      onChange={bind.onChange}
                      onKeyDown={bind.onKeyDown}
                      onSelect={bind.onSelect}
                      onClick={bind.onClick}
                      onBlur={bind.onBlur}
                      placeholder="Write a reply… Type : for emotes or use the picker"
                      required
                    />
                  )}
                </EmoteAutocomplete>
                <EmotePicker
                  emotes={emotes}
                  home={home}
                  disabled={busy}
                  manageHref={manageHref}
                  onPickEmote={(emote) =>
                    insertIntoField(
                      reply,
                      setReply,
                      replyRef.current,
                      emote.imageUrl
                        ? catalogEmoteShortcode(emote, emotes, home)
                        : emote.skinsUnicode ?? catalogEmoteShortcode(emote, emotes, home),
                    )
                  }
                />
              </div>
              <button type="submit" className="btn-primary" disabled={busy}>
                Submit reply
              </button>
            </form>
          )}
        </div>
      </div>
      {isRoot && post.replies.length > 0 && (
        <h2 className="thread-replies-heading">
          {post.replyCount} {post.replyCount === 1 ? 'reply' : 'replies'}
        </h2>
      )}
      <div className="post-tree">
        {post.replies.map((child) => (
          <PostNode
            key={child.id}
            post={child}
            forumId={forumId}
            baseUrl={baseUrl}
            threadLocked={threadLocked}
            onChanged={onChanged}
            createPost={createPost}
            updatePost={updatePost}
            deletePost={deletePost}
            getPostRevisions={getPostRevisions}
            setThreadLocked={setThreadLocked}
            setThreadPinned={setThreadPinned}
            setReplyPolicy={setReplyPolicy}
            reportPost={reportPost}
            emotes={emotes}
            home={home}
            manageHref={manageHref}
          />
        ))}
      </div>
    </div>
  );
}

export function ThreadPage() {
  const { serverDid: encodedDid, postId } = useParams<{ serverDid: string; postId: string }>();
  const { user } = useAuth();
  const { resolveBaseUrl, activeServers } = useServerRegistry();
  const { forums, recordVisit } = useForumNav();
  const [thread, setThread] = useState<Post | null>(null);
  const [emotes, setEmotes] = useState<CatalogEmote[]>([]);
  const [emoteHomeForum, setEmoteHomeForum] = useState<{
    id: string;
    slug: string;
    parentSlug?: string | null;
  } | null>(null);
  const [error, setError] = useState('');
  const [removed, setRemoved] = useState(false);
  const [ageStatus, setAgeStatus] = useState<NsfwAgeCheckStatus | null>(null);

  const serverDid = encodedDid ? decodeServerDid(encodedDid) : '';
  const baseUrl = resolveServerBaseUrl(serverDid, resolveBaseUrl);
  const client = useMemo(() => (baseUrl ? createContentClient(baseUrl) : null), [baseUrl]);
  const navForum = thread
    ? forums.find((item) => item.forumId === thread.forumId && item.serverDid === serverDid)
    : undefined;
  const emoteSettingsHref = serverDid
    ? emoteHomeForum ?? navForum
      ? `${forumSettingsPath(
          serverDid,
          emoteHomeForum?.slug ?? navForum?.slug ?? '',
          emoteHomeForum?.parentSlug ?? navForum?.parentSlug ?? undefined,
        )}#emotes`
      : `${serverSettingsPath(serverDid)}#emotes`
    : undefined;

  const load = () => {
    if (!postId || !client) return;
    setRemoved(false);
    client
      .getThread(postId)
      .then(setThread)
      .catch((err) => setError(err.message));
  };

  useEffect(load, [postId, client]);

  useEffect(() => {
    if (!client || !thread) return;
    void (async () => {
      let forumId = thread.forumId ?? undefined;
      let destPermissions: string[] | undefined;
      let homeForum: { id: string; slug: string; parentSlug?: string | null } | null = navForum
        ? { id: navForum.forumId, slug: navForum.slug, parentSlug: navForum.parentSlug }
        : null;
      if (!forumId && thread.watercoolerId) {
        const forums = await client.getForums().catch(() => []);
        const parent = forums.find((forum) => forum.watercoolers?.some((cooler) => cooler.id === thread.watercoolerId));
        forumId = parent?.id;
        destPermissions = parent?.viewerPermissions;
        if (parent) {
          homeForum = { id: parent.id, slug: parent.slug, parentSlug: parent.parentSlug };
        }
      } else if (navForum?.slug) {
        const forum = await client.getForum(navForum.slug, navForum.parentSlug ?? undefined).catch(() => null);
        destPermissions = forum?.viewerPermissions;
      }
      setEmoteHomeForum(homeForum && forumId ? { ...homeForum, id: forumId } : homeForum);
      const list = await loadContextEmotes({
        homeClient: client,
        forumId,
        destPermissions,
        homeServerDid: serverDid,
        activeServers,
      }).catch(() => [] as CatalogEmote[]);
      setEmotes(list);
    })();
    if (thread.forumId) {
      client.getNsfwAgeCheck(thread.forumId).then(setAgeStatus).catch(() => setAgeStatus(null));
    }
  }, [
    activeServers,
    client,
    navForum,
    serverDid,
    thread?.forumId,
    thread?.id,
    thread?.watercoolerId,
  ]);

  useEffect(() => {
    if (!user || !thread || !serverDid) return;
    const found = forums.find((forum) => forum.forumId === thread.forumId && forum.serverDid === serverDid);
    if (!found) return;
    recordVisit(found);
  }, [user, thread, serverDid, forums, recordVisit]);

  usePostRealtime({
    baseUrl,
    threadId: postId,
    onEvent: () => load(),
  });

  const handleDelete = async (targetPostId: string, hard?: boolean) => {
    if (!client) return;
    await client.deletePost(targetPostId, hard);
    if (hard && targetPostId === postId) {
      setThread(null);
      setRemoved(true);
      return;
    }
    load();
  };

  if (!baseUrl) return <div className="error-banner">Unknown content server.</div>;
  if (removed) {
    return (
      <div>
        <Link to="/" style={{ color: 'var(--muted)' }}>
          ← Back to forums
        </Link>
        <p className="card-meta">This post was permanently removed.</p>
      </div>
    );
  }
  if (!thread && !error) return <p>Loading thread...</p>;

  return (
    <div className="thread-page">
      <Link to="/" className="thread-back">
        ← Back to forums
      </Link>
      {error && <div className="error-banner">{error}</div>}
      {client && (ageStatus || (error && error.includes('Confirm your age'))) && (
        <NsfwAgeCheckPanel
          client={client}
          forumId={thread?.forumId ?? undefined}
          status={
            ageStatus ?? {
              enforced: true,
              requiredByServer: false,
              requiredByForum: true,
              minimumAge: 18,
              viewerAttested: false,
              blocksEntry: true,
            }
          }
          loggedIn={Boolean(user)}
          pendingNsfw={(thread?.attachments ?? []).some((att) => att.isNsfw && !att.url)}
          onCompleted={(next) => {
            setAgeStatus(next);
            setError('');
            load();
          }}
        />
      )}
      {thread && client && !ageStatus?.blocksEntry && (
        <>
          {thread.ancestors?.map((ancestor) => (
            <div key={ancestor.id} className="card cooler-quote" style={{ marginBottom: '0.5rem' }}>
              <UserByline presentation={ancestor.author} timestamp={ancestor.createdAt} showStatus={false} />
              <PostBody
                message={ancestor.message}
                emotes={emotes}
                home={{ forumId: emoteHomeForum?.id ?? thread.forumId, serverDid }}
              />
            </div>
          ))}
          <PostNode
          post={thread}
          forumId={thread.forumId}
          baseUrl={baseUrl}
          threadLocked={thread.isLocked}
          onChanged={load}
          createPost={client.createPost.bind(client)}
          updatePost={client.updatePost.bind(client)}
          deletePost={handleDelete}
          getPostRevisions={client.getPostRevisions.bind(client)}
          setThreadLocked={async (id, locked) => {
            await client.setThreadLocked(id, locked);
          }}
          setThreadPinned={async (id, pinned) => {
            await client.setThreadPinned(id, pinned);
          }}
          setReplyPolicy={async (id, policy) => {
            await client.setReplyPolicy(id, policy);
          }}
          reportPost={async (id, reason) => {
            await client.reportPost(id, reason);
          }}
          emotes={emotes}
          home={{ forumId: emoteHomeForum?.id ?? thread.forumId, serverDid }}
          manageHref={emoteSettingsHref}
        />
        </>
      )}
    </div>
  );
}
