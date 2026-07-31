import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { AuthProvider } from './AuthContext';
import { Layout } from './Layout';
import { CreateForumPage } from './pages/CreateForumPage';
import { FederationPage } from './pages/FederationPage';
import { ForumPage } from './pages/ForumPage';
import { HomePage } from './pages/HomePage';
import { LoginPage } from './pages/LoginPage';
import { RegisterPage } from './pages/RegisterPage';
import { ThreadPage } from './pages/ThreadPage';
import './Layout.css';

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Layout>
          <Routes>
            <Route path="/" element={<HomePage />} />
            <Route path="/login" element={<LoginPage />} />
            <Route path="/register" element={<RegisterPage />} />
            <Route path="/forums/new" element={<CreateForumPage />} />
            <Route path="/forums/:slug" element={<ForumPage />} />
            <Route path="/posts/:postId" element={<ThreadPage />} />
            <Route path="/federation" element={<FederationPage />} />
          </Routes>
        </Layout>
      </BrowserRouter>
    </AuthProvider>
  );
}
