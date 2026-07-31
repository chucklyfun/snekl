import { type FormEvent, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../AuthContext';

export function CreateForumPage() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [description, setDescription] = useState('');
  const [error, setError] = useState('');

  if (!user) {
    return (
      <div className="card">
        <p>Please <a href="/login">log in</a> to create a forum.</p>
      </div>
    );
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try {
      const forum = await api.createForum(name.trim(), slug.trim().toLowerCase(), description.trim());
      navigate(`/forums/${forum.slug}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create forum');
    }
  };

  const handleNameChange = (value: string) => {
    setName(value);
    if (!slug || slug === slugify(name)) {
      setSlug(slugify(value));
    }
  };

  return (
    <div className="card" style={{ maxWidth: 560 }}>
      <h1 className="page-title">Create forum</h1>
      {error && <div className="error-banner">{error}</div>}
      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label htmlFor="name">Name</label>
          <input id="name" value={name} onChange={(e) => handleNameChange(e.target.value)} required />
        </div>
        <div className="form-group">
          <label htmlFor="slug">URL slug</label>
          <input id="slug" value={slug} onChange={(e) => setSlug(e.target.value)} required pattern="[a-z0-9-]+" />
        </div>
        <div className="form-group">
          <label htmlFor="description">Description</label>
          <textarea id="description" value={description} onChange={(e) => setDescription(e.target.value)} />
        </div>
        <button type="submit" className="btn-primary">Create forum</button>
      </form>
    </div>
  );
}

function slugify(value: string) {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}
