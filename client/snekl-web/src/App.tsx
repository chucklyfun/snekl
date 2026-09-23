import { BrowserRouter, Route, Routes } from 'react-router';
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
import { CreateWatercoolerPage } from './pages/CreateWatercoolerPage';
import { WatercoolerPage } from './pages/WatercoolerPage';
import { WatercoolerSettingsPage } from './pages/WatercoolerSettingsPage';

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
              <Route path="/forums/new" element={<CreateForumPage />} />
              <Route path="/forums" element={<BrowseForumsPage />} />
              <Route path="/s/:serverDid/forums/new" element={<CreateForumPage />} />
              <Route path="/s/:serverDid/coolers/new" element={<CreateWatercoolerPage />} />
              <Route path="/s/:serverDid/coolers/:slug/settings" element={<WatercoolerSettingsPage />} />
              <Route path="/s/:serverDid/coolers/:slug" element={<WatercoolerPage />} />
              <Route
                path="/s/:serverDid/forums/:parentSlug/:slug/coolers/:coolerSlug/settings"
                element={<WatercoolerSettingsPage />}
              />
              <Route
                path="/s/:serverDid/forums/:slug/coolers/:coolerSlug/settings"
                element={<WatercoolerSettingsPage />}
              />
              <Route path="/s/:serverDid/forums/:parentSlug/:slug/coolers/:coolerSlug" element={<WatercoolerPage />} />
              <Route path="/s/:serverDid/forums/:slug/coolers/:coolerSlug" element={<WatercoolerPage />} />
              <Route path="/s/:serverDid/forums/:parentSlug/:slug/settings" element={<ForumSettingsPage />} />
              <Route path="/s/:serverDid/forums/:slug/settings" element={<ForumSettingsPage />} />
              <Route path="/s/:serverDid/forums/:parentSlug/:slug" element={<ForumPage />} />
              <Route path="/s/:serverDid/forums/:slug" element={<ForumPage />} />
              <Route path="/s/:serverDid/posts/:postId" element={<ThreadPage />} />
              <Route path="/lists/:listId/members" element={<ListMembersPage />} />
              <Route path="/lists/:listId" element={<ListsPage />} />
              <Route path="/lists" element={<ListsPage />} />
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
