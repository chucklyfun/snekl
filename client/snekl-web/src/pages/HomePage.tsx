import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import type { Forum, NodeInfo } from '../types';

export function HomePage() {
  const [forums, setForums] = useState<Forum[]>([]);
  const [node, setNode] = useState<NodeInfo | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    Promise.all([api.getForums(), api.getNode()])
      .then(([forumList, nodeInfo]) => {
        setForums(forumList);
        setNode(nodeInfo);
      })
      .catch((err) => setError(err.message));
  }, []);

  return (
    <div>
      <h1 className="page-title">Forums</h1>
      <p className="page-subtitle">
        {node ? (
          <>Browsing <strong>{node.name}</strong> — federated content from peer nodes appears here automatically.</>
        ) : (
          'Loading node...'
        )}
      </p>

      {error && <div className="error-banner">{error}</div>}

      {forums.length === 0 && !error && (
        <div className="card">
          <p>No forums yet. Sign up and create the first one, or subscribe to a peer node in Federation.</p>
        </div>
      )}

      {forums.map((forum) => (
        <Link key={forum.id} to={`/forums/${forum.slug}`} className="card card-link">
          <h2 className="card-title">
            {forum.name}
            {forum.isFederated && <span className="badge badge-federated">federated</span>}
          </h2>
          <p>{forum.description || 'No description'}</p>
          <div className="card-meta">
            {forum.postCount} threads · created {new Date(forum.createdAt).toLocaleDateString()}
            {forum.originNodeUrl && <> · from {forum.originNodeUrl}</>}
          </div>
        </Link>
      ))}
    </div>
  );
}
