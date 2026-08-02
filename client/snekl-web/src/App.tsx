import { BrowserRouter, Route, Routes } from 'react-router';
import { AdminAuthProvider, RequireAdmin } from './AdminAuthContext';
import { AdminLayout } from './AdminLayout';
import { AuthProvider } from './AuthContext';
import { Layout } from './Layout';
import { ServerRegistryProvider } from './ServerRegistryContext';
import { AdminDashboardPage } from './pages/admin/AdminDashboardPage';
import { AdminLoginPage } from './pages/admin/AdminLoginPage';
import { CreateForumPage } from './pages/CreateForumPage';
import { FederationPage } from './pages/FederationPage';
import { ListsPage } from './pages/ListsPage';
import { ProfilePage } from './pages/ProfilePage';
import { ForumPage } from './pages/ForumPage';
import { HomePage } from './pages/HomePage';
import { LoginPage } from './pages/LoginPage';
import { RegisterPage } from './pages/RegisterPage';
import { ServersPage } from './pages/ServersPage';
import { ServerSettingsPage } from './pages/ServerSettingsPage';
import { ForumSettingsPage } from './pages/ForumSettingsPage';
import { ThreadPage } from './pages/ThreadPage';
import './Layout.css';

export default function App() {
  return (
    <ServerRegistryProvider>
      <AuthProvider>
        <AdminAuthProvider>
          <BrowserRouter>
            <Routes>
              <Route path="/admin/login" element={<AdminLoginPage />} />
              <Route
                path="/admin"
                element={
                  <RequireAdmin>
                    <AdminLayout />
                  </RequireAdmin>
                }
              >
                <Route index element={<AdminDashboardPage />} />
              </Route>
              <Route
                path="/*"
                element={
                  <Layout>
                    <Routes>
                      <Route path="/" element={<HomePage />} />
                      <Route path="/login" element={<LoginPage />} />
                      <Route path="/register" element={<RegisterPage />} />
                      <Route path="/servers" element={<ServersPage />} />
                      <Route path="/s/:serverDid/settings" element={<ServerSettingsPage />} />
                      <Route path="/s/:serverDid/forums/new" element={<CreateForumPage />} />
                      <Route path="/s/:serverDid/forums/:parentSlug/:slug/settings" element={<ForumSettingsPage />} />
                      <Route path="/s/:serverDid/forums/:slug/settings" element={<ForumSettingsPage />} />
                      <Route path="/s/:serverDid/forums/:parentSlug/:slug" element={<ForumPage />} />
                      <Route path="/s/:serverDid/forums/:slug" element={<ForumPage />} />
                      <Route path="/s/:serverDid/posts/:postId" element={<ThreadPage />} />
                      <Route path="/federation" element={<FederationPage />} />
                      <Route path="/lists" element={<ListsPage />} />
                      <Route path="/users/:username" element={<ProfilePage />} />
                    </Routes>
                  </Layout>
                }
              />
            </Routes>
          </BrowserRouter>
        </AdminAuthProvider>
      </AuthProvider>
    </ServerRegistryProvider>
  );
}
