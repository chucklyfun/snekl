import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router';
import { AuthProvider } from './AuthContext';
import { Layout } from './Layout';
import { ServerRegistryProvider } from './ServerRegistryContext';
import { FollowsProvider } from './FollowsContext';
import { ForumNavProvider } from './ForumNavContext';
import { CreateForumPage } from './pages/CreateForumPage';
import { BrowseForumsPage } from './pages/BrowseForumsPage';
import { ListsPage } from './pages/ListsPage';
import { ListMembersPage } from './pages/ListMembersPage';
import { ProfilePage } from './pages/ProfilePage';
import { ForumPage } from './pages/ForumPage';
import { HomePage } from './pages/HomePage';
import { SearchPage } from './pages/SearchPage';
import { ThreadPage } from './pages/ThreadPage';
import { AccountSettingsPage } from './pages/AccountSettingsPage';
import { ForgotPasswordPage } from './pages/ForgotPasswordPage';
import { LoginPage } from './pages/LoginPage';
import { RegisterPage } from './pages/RegisterPage';
import { ResetPasswordPage } from './pages/ResetPasswordPage';
import { VerifyEmailPage } from './pages/VerifyEmailPage';
import { ServersPage } from './pages/ServersPage';
import { AdminsPage } from './pages/AdminsPage';
import { TempPage } from './pages/TempPage';
import { ServerSettingsPage } from './pages/ServerSettingsPage';
import { ServerOwnerSettingsPage } from './pages/ServerOwnerSettingsPage';
import { ForumSettingsPage } from './pages/ForumSettingsPage';
import { AuthorizeExtensionPage } from './pages/AuthorizeExtensionPage';
import { ExtensionsPage } from './pages/ExtensionsPage';
import { FollowingPage } from './pages/FollowingPage';
import { MessagesPage } from './pages/MessagesPage';
import { AlertsPage } from './pages/AlertsPage';
import { CreateWatercoolerPage } from './pages/CreateWatercoolerPage';
import { WatercoolerPage } from './pages/WatercoolerPage';
import { WatercoolerSettingsPage } from './pages/WatercoolerSettingsPage';
import { CommissionsPage } from './pages/CommissionsPage';

function LegacyForumRedirect() {
  const location = useLocation();
  const pathname = location.pathname.replace(/\/forums(?=\/|$)/g, '/nests');
  return <Navigate to={`${pathname}${location.search}${location.hash}`} replace />;
}

export default function App() {
  return (
    <ServerRegistryProvider>
      <AuthProvider>
        <BrowserRouter>
          <FollowsProvider>
          <ForumNavProvider>
          <Layout>
            <Routes>
              <Route path="/" element={<HomePage />} />
              <Route path="/search" element={<SearchPage />} />
              <Route path="/login" element={<LoginPage />} />
              <Route path="/register" element={<RegisterPage />} />
              <Route path="/account" element={<AccountSettingsPage />} />
              <Route path="/following" element={<FollowingPage />} />
              <Route path="/messages/:conversationId" element={<MessagesPage />} />
              <Route path="/messages" element={<MessagesPage />} />
              <Route path="/alerts" element={<AlertsPage />} />
              <Route path="/forgot-password" element={<ForgotPasswordPage />} />
              <Route path="/reset-password" element={<ResetPasswordPage />} />
              <Route path="/verify-email" element={<VerifyEmailPage />} />
              <Route path="/servers" element={<ServersPage />} />
              <Route path="/admin" element={<AdminsPage />} />
              <Route path="/admin/*" element={<AdminsPage />} />
              <Route path="/hub" element={<AdminsPage />} />
              <Route path="/temp" element={<TempPage />} />
              <Route path="/extensions" element={<ExtensionsPage />} />
              <Route path="/extensions/authorize" element={<AuthorizeExtensionPage />} />
              <Route path="/s/:serverDid/settings" element={<ServerSettingsPage />} />
              <Route path="/s/:serverDid/owner" element={<ServerOwnerSettingsPage />} />
              <Route path="/nests/new" element={<CreateForumPage />} />
              <Route path="/nests" element={<BrowseForumsPage />} />
              <Route path="/s/:serverDid/nests/new" element={<CreateForumPage />} />
              <Route path="/s/:serverDid/coolers/new" element={<CreateWatercoolerPage />} />
              <Route path="/s/:serverDid/coolers/:slug/settings" element={<WatercoolerSettingsPage />} />
              <Route path="/s/:serverDid/coolers/:slug" element={<WatercoolerPage />} />
              <Route
                path="/s/:serverDid/nests/:parentSlug/:slug/coolers/:coolerSlug/settings"
                element={<WatercoolerSettingsPage />}
              />
              <Route
                path="/s/:serverDid/nests/:slug/coolers/:coolerSlug/settings"
                element={<WatercoolerSettingsPage />}
              />
              <Route path="/s/:serverDid/nests/:parentSlug/:slug/coolers/:coolerSlug" element={<WatercoolerPage />} />
              <Route path="/s/:serverDid/nests/:slug/coolers/:coolerSlug" element={<WatercoolerPage />} />
              <Route path="/s/:serverDid/nests/:parentSlug/:slug/settings" element={<ForumSettingsPage />} />
              <Route path="/s/:serverDid/nests/:slug/settings" element={<ForumSettingsPage />} />
              <Route path="/s/:serverDid/nests/:parentSlug/:slug" element={<ForumPage />} />
              <Route path="/s/:serverDid/nests/:slug" element={<ForumPage />} />
              <Route path="/forums/*" element={<LegacyForumRedirect />} />
              <Route path="/forums" element={<LegacyForumRedirect />} />
              <Route path="/s/:serverDid/forums/*" element={<LegacyForumRedirect />} />
              <Route path="/s/:serverDid/posts/:postId" element={<ThreadPage />} />
              <Route path="/lists/:listId/members" element={<ListMembersPage />} />
              <Route path="/lists/:listId" element={<ListsPage />} />
              <Route path="/lists" element={<ListsPage />} />
              <Route path="/commissions" element={<CommissionsPage />} />
              <Route path="/users/:username" element={<ProfilePage />} />
            </Routes>
          </Layout>
          </ForumNavProvider>
          </FollowsProvider>
        </BrowserRouter>
      </AuthProvider>
    </ServerRegistryProvider>
  );
}
