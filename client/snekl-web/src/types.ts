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
}

export interface AuthMeResponse {
  user: UserPresentation;
  isSuperUser: boolean;
  serverPermissions: string[];
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
}

export interface PostRevision {
  id: string;
  message: string;
  editedByUserId: string;
  editedByUsername: string;
  createdAt: string;
}

export interface Post {
  id: string;
  forumId: string;
  parentPostId?: string | null;
  author: UserPresentation;
  message: string;
  replyCount: number;
  upvoteCount: number;
  viewerHasUpvoted: boolean;
  originNodeUrl?: string | null;
  isFederated: boolean;
  isDeleted: boolean;
  isLocked: boolean;
  isPinned: boolean;
  editedAt?: string | null;
  createdAt: string;
  replies: Post[];
  viewerActions?: PostViewerActions | null;
}

export interface RecentThread {
  post: Post;
  forumSlug: string;
  forumName: string;
  parentForumSlug?: string | null;
  ownerBaseUrl: string;
  ownerDid: string;
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
}

export interface NodeInfo {
  id: string;
  did: string;
  name: string;
  baseUrl: string;
  description: string;
  createdAt: string;
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
}

export interface SiteAdmin {
  id: string;
  username: string;
  createdAt: string;
}
