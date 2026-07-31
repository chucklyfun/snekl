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
  createdBy?: User | null;
  originNodeUrl?: string | null;
  isFederated: boolean;
  postCount: number;
  createdAt: string;
}

export interface Post {
  id: string;
  forumId: string;
  parentPostId?: string | null;
  author: User;
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
