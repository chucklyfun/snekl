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
}

export interface Post {
  id: string;
  forumId: string;
  parentPostId?: string | null;
  author: UserPresentation;
  message: string;
  replyCount: number;
  originNodeUrl?: string | null;
  isFederated: boolean;
  createdAt: string;
  replies: Post[];
}

export interface NodeInfo {
  id: string;
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
