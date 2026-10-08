import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { useAuth } from '../AuthContext';
import { CreateForumComposer } from '../components/CreateForumComposer';
import { decodeServerDid, forumPath } from '../serverDid';
import { useCreateForumEligibility } from '../useCreateForumEligibility';

export function CreateForumPage() {
  const { serverDid: encodedDid } = useParams<{ serverDid?: string }>();
  const [searchParams] = useSearchParams();
  const { user } = useAuth();
  const navigate = useNavigate();
  const urlServerDid = encodedDid ? decodeServerDid(encodedDid) : undefined;
  const {
    eligibleServers,
    checkErrors,
    permissionsLoading,
    urlServerCanCreate,
    urlServerCanRequest,
  } = useCreateForumEligibility(urlServerDid);

  if (!user) {
    return (
      <div className="card">
        <p>
          Please <Link to="/login">log in</Link> to create a nest.
        </p>
      </div>
    );
  }

  return (
    <div className="card" style={{ maxWidth: 560 }}>
      <CreateForumComposer
        eligibleServers={eligibleServers}
        checkErrors={checkErrors}
        permissionsLoading={permissionsLoading}
        urlServerDid={urlServerDid}
        urlServerCanCreate={urlServerCanCreate}
        urlServerCanRequest={urlServerCanRequest}
        initialParentForumId={searchParams.get('parent') ?? ''}
        onCreated={(forum, serverDid) => {
          navigate(
            forumPath(serverDid, forum.slug, searchParams.get('parentSlug') ?? forum.parentSlug ?? undefined),
          );
        }}
      />
    </div>
  );
}
