export interface Overlay {
  kind: string;
  emoji?: string | null;
  iconUrl?: string | null;
  color?: string | null;
  position?: string | null;
  ariaLabel?: string | null;
}

export interface UserFlair {
  emoji?: string | null;
  iconUrl?: string | null;
  label?: string | null;
  nameColor?: string | null;
  nameStyle?: string | null;
}

export interface UserStatus {
  preset: string;
  message?: string | null;
  emoji?: string | null;
  iconUrl?: string | null;
  linkUrl?: string | null;
  eventName?: string | null;
  expiresAt: string;
  avatarOverlay?: Overlay | null;
  displayNameSuffix?: string | null;
  nameColorOverride?: string | null;
}

export interface ListAnnotation {
  listId: string;
  listName: string;
  label: string;
  severity: number;
  tooltipText: string;
  flairEmoji?: string | null;
  flairIconUrl?: string | null;
  usernameColor?: string | null;
  usernameStyle?: string | null;
  avatarOverlay?: Overlay | null;
  addedAt: string;
}

export interface UserPresentation {
  id: string;
  username: string;
  displayName: string;
  bio: string;
  originNodeUrl?: string | null;
  isFederated: boolean;
  effectiveNameColor?: string | null;
  effectiveNameStyle?: string | null;
  effectiveDisplayName: string;
  flair?: UserFlair | null;
  activeStatus?: UserStatus | null;
  annotations: ListAnnotation[];
  avatarOverlay?: Overlay | null;
  isBot?: boolean;
  did?: string | null;
}

export interface User {
  id: string;
  username: string;
  displayName: string;
  email: string;
  bio: string;
  originNodeUrl?: string | null;
  isFederated: boolean;
}

export interface ForumSummary {
  id: string;
  name: string;
  slug: string;
  postCount: number;
  avatarUrl?: string | null;
}

export interface Forum {
  id: string;
  name: string;
  slug: string;
  description: string;
  createdBy?: UserPresentation | null;
  originNodeUrl?: string | null;
  isFederated: boolean;
  postCount: number;
  createdAt: string;
  parentForumId?: string | null;
  parentSlug?: string | null;
  subforums?: ForumSummary[];
  viewerPermissions?: string[];
  allowAliases?: boolean;
  approvalStatus?: string;
  viewerCanApprove?: boolean;
  viewerIsMember?: boolean;
  viewerCanManage?: boolean;
  viewerIsFavorite?: boolean;
  watercoolers?: WatercoolerSummary[];
  contentBlocks?: ForumContentBlock[];
  nsfwAgeCheckRequired?: boolean;
  nsfwAgeCheckRequiredByServer?: boolean;
  nsfwAgeCheckEnforced?: boolean;
  nsfwMinimumAge?: number;
  viewerNsfwAgeAttested?: boolean;
  nsfwAgeCheckExplanation?: string | null;
  nsfwAgeCheckBlocksEntry?: boolean;
  nsfwAgeCheckGateExplanation?: string | null;
  avatarUrl?: string | null;
  bannerUrl?: string | null;
  ownerUsername?: string | null;
  isViewerOwner?: boolean;
}

export interface WatercoolerSummary {
  id: string;
  name: string;
  slug: string;
  postCount: number;
  allowUpvotes: boolean;
  newestAtBottom: boolean;
  approvalStatus: string;
  viewerCanView: boolean;
  viewerCanSpeak?: boolean;
}

export interface Watercooler {
  id: string;
  name: string;
  slug: string;
  description: string;
  createdBy?: UserPresentation | null;
  parentForumId?: string | null;
  parentForumSlug?: string | null;
  parentForumName?: string | null;
  allowUpvotes: boolean;
  newestAtBottom: boolean;
  approvalStatus: string;
  createdAt: string;
  postCount: number;
  viewerPermissions?: string[];
  viewerCanApprove?: boolean;
  viewerCanManage?: boolean;
}

export interface WatercoolerRole {
  id: string;
  name: string;
  color?: string | null;
  position: number;
  permissions: string[];
  isEveryone: boolean;
}

export interface ForumContentBlock {
  id: string;
  typeKey: string;
  watercoolerId?: string | null;
  slot: string;
  position: number;
}

export interface AuthMeResponse {
  user: UserPresentation;
  isSuperUser: boolean;
  serverPermissions: string[];
  isServerOwner?: boolean;
}

export interface SuperUser {
  userId: string;
  username: string;
  displayName: string;
  grantedBySiteAdminId: string;
  grantedAt: string;
  note?: string | null;
}

export interface ServerRole {
  id: string;
  name: string;
  color?: string | null;
  position: number;
  permissions: string[];
  isEveryone: boolean;
}

export interface ServerMember {
  userId: string;
  username: string;
  displayName: string;
  roles: ServerRole[];
}

export interface ServerPermissionsInfo {
  isSuperUser: boolean;
  permissions: string[];
  roles?: ServerRole[];
  nsfwAgeCheckRequired?: boolean;
  nsfwMinimumAge?: number;
  viewerNsfwAgeAttested?: boolean;
  isServerOwner?: boolean;
}

export interface NsfwAgeCheckStatus {
  enforced: boolean;
  requiredByServer: boolean;
  requiredByForum: boolean;
  minimumAge: number;
  viewerAttested: boolean;
  forumExplanation?: string | null;
  blocksEntry?: boolean;
}

export interface ForumRole {
  id: string;
  forumId: string;
  name: string;
  color?: string | null;
  position: number;
  permissions: string[];
  isEveryone: boolean;
  inheritFromRoleId?: string | null;
  isOwner?: boolean;
}

export interface ForumMember {
  userId: string;
  username: string;
  displayName: string;
  roles: ForumRole[];
  flair?: ForumFlairTemplate | null;
  alias?: string | null;
}

export interface ForumBan {
  id: string;
  userId: string;
  username: string;
  displayName: string;
  reason?: string | null;
  expiresAt?: string | null;
  bannedByUsername: string;
  createdAt: string;
}

export interface PostReport {
  id: string;
  postId: string;
  postPreview: string;
  postAuthorUsername: string;
  reporterUserId: string;
  reporterUsername: string;
  reason: string;
  status: string;
  createdAt: string;
  resolvedAt?: string | null;
}

export interface ForumFlairTemplate {
  id: string;
  forumId: string;
  emoji?: string | null;
  iconUrl?: string | null;
  label?: string | null;
  nameColor?: string | null;
  nameStyle?: string | null;
  position: number;
  inheritFromTemplateId?: string | null;
}

export interface PostViewerActions {
  canEdit: boolean;
  canSoftDelete: boolean;
  canHardDelete: boolean;
  canViewHistory: boolean;
  canLock?: boolean;
  canUnlock?: boolean;
  canPin?: boolean;
  canUnpin?: boolean;
  canReport?: boolean;
  canMarkNsfwMedia?: boolean;
  canReply?: boolean;
  canSetReplyPolicy?: boolean;
  canUpvote?: boolean;
}

export interface PostRevision {
  id: string;
  message: string;
  editedByUserId: string;
  editedByUsername: string;
  createdAt: string;
}

export interface ChangeLogEntry {
  id: string;
  scope: string;
  forumId?: string | null;
  watercoolerId?: string | null;
  entityType: string;
  entityId: string;
  action: string;
  actorUserId?: string | null;
  actorUsername: string;
  summary: string;
  beforeJson?: string | null;
  afterJson?: string | null;
  createdAt: string;
}

export interface MediaAttachment {
  id: string;
  url?: string | null;
  contentType: string;
  byteSize: number;
  width?: number | null;
  height?: number | null;
  isNsfw?: boolean;
  coverText?: string | null;
  censorUrl?: string | null;
}

export interface TopReplyPreview {
  id: string;
  author: UserPresentation;
  message: string;
  upvoteCount: number;
  createdAt: string;
}

export interface QuotedPost {
  id: string;
  author: UserPresentation;
  message: string;
  watercoolerId: string;
  createdAt: string;
  viewerCanView: boolean;
}

export interface Post {
  id: string;
  forumId?: string | null;
  watercoolerId?: string | null;
  parentPostId?: string | null;
  quotedPostId?: string | null;
  quoteCount?: number;
  quotedPost?: QuotedPost | null;
  ancestors?: Post[] | null;
  author: UserPresentation;
  title?: string | null;
  message: string;
  replyCount: number;
  upvoteCount: number;
  viewerHasUpvoted: boolean;
  originNodeUrl?: string | null;
  isFederated: boolean;
  isDeleted: boolean;
  isLocked: boolean;
  isPinned: boolean;
  replyPolicy?: string | null;
  editedAt?: string | null;
  createdAt: string;
  replies: Post[];
  viewerActions?: PostViewerActions | null;
  attachments?: MediaAttachment[];
  topReplyPreview?: TopReplyPreview | null;
  reactions?: PostReaction[];
}

export interface PostReaction {
  key: string;
  emoteId?: string | null;
  code?: string | null;
  emoji?: string | null;
  imageUrl?: string | null;
  count: number;
  viewerReacted: boolean;
}

export interface CatalogEmote {
  id: string;
  code: string;
  name: string;
  imageUrl: string;
  forumId?: string | null;
  forumSlug?: string | null;
  forumName?: string | null;
  serverDid?: string | null;
  serverPrefix?: string | null;
  isAnimated: boolean;
  createdAt: string;
  skinsUnicode?: string | null;
  canInsert?: boolean;
  sortOrder?: number;
  highlighted?: boolean;
}

export interface RecentThread {
  post: Post;
  forumSlug: string;
  forumName: string;
  parentForumSlug?: string | null;
  ownerBaseUrl: string;
  ownerDid: string;
}

export interface SearchHit {
  thread: RecentThread;
  snippet: string;
  matchedInReply: boolean;
}

export interface NetworkServer {
  did: string;
  nodeId: string;
  baseUrl: string;
  name: string;
  isSelf: boolean;
}

export interface AggregatedPost extends Post {
  serverDid: string;
  serverName: string;
  serverBaseUrl: string;
  forumSlug: string;
  forumName: string;
  parentForumSlug?: string | null;
  imagePreviewUrl?: string | null;
  nsfwCover?: string | null;
}

export interface RateLimitRule {
  pathPrefix: string;
  method: string;
  limit: number;
  windowSeconds: number;
}

export interface RateLimitPolicy {
  enabled: boolean;
  partition: string;
  rules: RateLimitRule[];
}

export interface NodeInfo {
  id: string;
  did: string;
  name: string;
  baseUrl: string;
  description: string;
  createdAt: string;
  /** Accepted hub for this origin, when registered with a federation. */
  hubBaseUrl?: string | null;
  /** HTTP rate limits for this content API. Partition is client IP. */
  rateLimiting?: RateLimitPolicy | null;
  ownerUsername?: string | null;
  ownerUserId?: string | null;
}

export interface Peer {
  id: string;
  baseUrl: string;
  name: string;
  remoteNodeId: string;
  isActive: boolean;
  subscribedAt: string;
}

export interface AuthResponse {
  token: string;
  user: User;
}

export interface PersonCluster {
  id: string;
  did: string;
  canonicalUsername: string;
  displayName: string;
  email: string;
  emailVerified: boolean;
  bio: string;
  primaryAuthServerUrl: string;
  disambiguationTag?: string | null;
  displayLabel: string;
  identities: Array<{
    id: string;
    authServerUrl: string;
    isPrimary: boolean;
    hasLocalPassword: boolean;
  }>;
}

export interface RingSession {
  ringSessionId: string;
  accessToken: string;
  expiresAt: string;
  person: PersonCluster;
  partnerSessions: Array<{
    authServerUrl: string;
    accessToken: string;
    expiresAt: string;
  }>;
  generatedKeyPrivatePkcs8Base64?: string | null;
}

export interface ContentServerMembership {
  serverDid: string;
  baseUrl: string;
  name: string;
  nodeId?: string | null;
  status: string;
  subscribedAt: string;
}

export interface ContentServerStatusPreset {
  preset: string;
  label: string;
  description: string;
  includedInFeed: boolean;
  sortFirst: boolean;
}

export interface FederationMembership {
  baseUrl: string;
  name: string;
  authIssuer?: string | null;
  status: string;
  subscribedAt: string;
}

export interface FederationOriginGroup {
  baseUrl: string;
  name: string;
  origins: ContentServerMembership[];
}

export interface HubFeedCard {
  schemaVersion: number;
  postId: string;
  forumId: string;
  forumSlug: string;
  forumName: string;
  serverDid: string;
  originBaseUrl: string;
  originPostUrl: string;
  title?: string | null;
  excerpt: string;
  imagePreviewUrl?: string | null;
  authorDid: string;
  authorDisplayName: string;
  createdAt: string;
  editedAt?: string | null;
  visibility: string;
  score: number;
  commentCount: number;
  mapVersion: number;
  event: string;
  eventAt: string;
  nsfwCover?: string | null;
}

export interface HubSearchHit {
  thread: HubFeedCard;
  snippet: string;
}

export interface AggregatedSearchHit extends SearchHit {
  serverName: string;
  serverBaseUrl: string;
  serverDid: string;
}

export interface ForumFavorite {
  serverDid: string;
  forumId: string;
  name: string;
  slug: string;
  parentSlug?: string | null;
  serverName: string;
  addedAt: string;
}

export interface Follow {
  did: string;
  displayName?: string | null;
  username?: string | null;
  addedAt: string;
}

export interface DirectMessagePeer {
  id: string;
  username: string;
  displayName: string;
  did?: string | null;
}

export interface DirectMessageForumOption {
  id: string;
  name: string;
  slug: string;
  parentSlug?: string | null;
}

export interface DirectConversationRequest {
  id: string;
  requester: DirectMessagePeer;
  target: DirectMessagePeer;
  forum: DirectMessageForumOption;
  message?: string | null;
  status: string;
  createdAt: string;
  resolvedAt?: string | null;
  conversationId?: string | null;
}

export interface DirectConversationSummary {
  id: string;
  peer: DirectMessagePeer;
  originForum?: DirectMessageForumOption | null;
  lastMessagePreview?: string | null;
  updatedAt: string;
}

export interface DirectMessage {
  id: string;
  conversationId: string;
  author: DirectMessagePeer;
  body: string;
  createdAt: string;
}

export interface DirectConversation {
  id: string;
  peer: DirectMessagePeer;
  originForum?: DirectMessageForumOption | null;
  createdAt: string;
  messages: DirectMessage[];
}

export interface DirectMessageEligibility {
  target: DirectMessagePeer;
  canRequest: boolean;
  reason?: string | null;
  existingConversationId?: string | null;
  pendingRequest?: DirectConversationRequest | null;
  eligibleForums: DirectMessageForumOption[];
}

export interface DirectMessageInbox {
  incomingRequests: DirectConversationRequest[];
  outgoingRequests: DirectConversationRequest[];
  conversations: DirectConversationSummary[];
}

export interface DirectMessageForumPreference {
  forum: DirectMessageForumOption;
  mode: 'none' | 'ignore' | 'reject' | string;
}

export interface DirectMessageForumPreferences {
  forums: DirectMessageForumPreference[];
}

export interface FederationConfig {
  schema: string;
  name: string;
  baseUrl: string;
  auth: { issuer: string; jwks: string };
  mapVersion: number;
  servers: Array<{ did: string; baseUrl: string; name: string }>;
  feed?: string | null;
}

export interface AdminBootstrap {
  kind: 'auth' | 'hub' | 'content';
  name: string;
  publicBaseUrl: string;
  did?: string | null;
  authLoginUrl: string;
}

export interface AggregatedForum extends Forum {
  serverDid: string;
  serverName: string;
  serverBaseUrl: string;
  serverStatus: string;
}

export interface StatusPreset {
  preset: string;
  label: string;
  defaultMessage: string;
  defaultEmoji?: string | null;
  defaultOverlayKind: string;
  defaultOverlayColor?: string | null;
  defaultDurationHours: number;
}

export interface CuratedList {
  id: string;
  name: string;
  description: string;
  defaultBehavior: string;
  defaultFlairEmoji?: string | null;
  defaultIconUrl?: string | null;
  isPublic: boolean;
  entryCount: number;
  subscriberCount: number;
  createdAt: string;
  isOwner: boolean;
  isViewerSubscribed?: boolean;
  ownerUsername?: string | null;
  viewerPriority?: number | null;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface ListEntry {
  id: string;
  listId: string;
  targetUserId: string;
  targetUsername: string;
  label: string;
  severity: number;
  tooltipText: string;
  flairEmoji?: string | null;
  flairIconUrl?: string | null;
  addedAt: string;
  isIgnoredByViewer?: boolean;
}

export interface UserListEntryHit {
  entryId: string;
  listId: string;
  listName: string;
  listOwnerUsername?: string | null;
  targetUsername: string;
  label: string;
  severity: number;
  tooltipText: string;
  flairEmoji?: string | null;
  flairIconUrl?: string | null;
  isViewerSubscribed: boolean;
  viewerPriority?: number | null;
  isIgnoredByViewer: boolean;
  appliesOnPosts: boolean;
  listDefaultBehavior: string;
}

export interface SiteAdmin {
  id: string;
  username: string;
  createdAt: string;
}

export interface ContentBlockPermission {
  name: string;
  label: string;
  description?: string | null;
  scope: string;
}

export interface ContentBlockType {
  key: string;
  name: string;
  description?: string | null;
  permissions: ContentBlockPermission[];
}

export interface ExtensionInstall {
  id: string;
  clientId: string;
  appName: string;
  adminUrl?: string | null;
  iframeUrl?: string | null;
  resourceType: string;
  forumId?: string | null;
  forumName?: string | null;
  serverPermissions: string[];
  forumPermissions: string[];
  installedByUserId: string;
  installedByDid: string;
  createdAt: string;
  rateLimiting?: RateLimitPolicy | null;
}

export interface ExtensionInstallOptions {
  canInstallServer: boolean;
  serverPermissions: string[];
  forums: Array<{
    id: string;
    name: string;
    slug: string;
    canInstall: boolean;
    forumPermissions: string[];
  }>;
  existingInstall?: ExtensionInstall | null;
}
