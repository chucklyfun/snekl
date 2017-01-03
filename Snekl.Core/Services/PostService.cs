using ServiceStack.Data;
using ServiceStack.OrmLite;
using Snekl.Core.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snekl.Core.Services
{
    public interface IPostService
    {
        IEnumerable<PostTree> GetPostsByAnchorId(long anchorId, int startRowIndex, int maximumRows, out QueryResponse response);

        IEnumerable<PostTree> GetPostTree(long postId, int startRowIndex, int maximumRows, out QueryResponse response);

        IEnumerable<PostTree> GetPostReferenceTree(long postId, int startRowIndex, int maximumRows, out QueryResponse response);

        IEnumerable<PostTree> GetUserTree(long userId, int startRowIndex, int maximumRows, out QueryResponse response);

        IEnumerable<PostTree> GetUserReferenceTree(long userId, int startRowIndex, int maximumRows, out QueryResponse response);
    }

    public class PostService : IPostService
    {
        private IDbConnectionFactory _dbConnectionFactory;

        public PostService(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public const string AnchorFilter = @" join anchor_link on post.internal_id = anchor_link.post_id
                    where anchor_link.anchor_id = @Id ";

        public const string PostReferenceFilter = @" join post_link on post.internal_id = post_link.post_id
                    where post_link.anchor_id = @Id ";

        public const string ParentPostFilter = @" WHERE post.internal_id = @Id ";

        public const string UserFilter = @" WHERE post.user_id = @Id ";

        public const string UserReferenceFilter = 
            @" join anchor_link on post.internal_id = anchor_link.post_id
                    WHERE post.user_id = @Id ";

        public const string Paged = @" OFFSET @StartRowIndex LIMIT @MaximumRows ";

        public const string GroupedPostsQuery =  @" cte_Grouped AS
                    (
                        SELECT cte_Posts.internal_id,
                            cte_Posts.parent_id,
                            MIN(cte_Posts.r) AS r
                        FROM cte_Posts
                        GROUP BY cte_Posts.internal_id,
    	                    cte_Posts.parent_id
                    ) ";

        public const string ParentPostQuery = @"
            SELECT post.internal_id,
                post.parent_id,
                0 AS r
            FROM post ";

        public const string ChildPostsQuery = @" 
            select c.internal_id,
                c.parent_id AS parent_id,
                (cte_Posts.r + 1) AS r
                from post c
            join cte_Posts on c.parent_id = cte_Posts.internal_id ";

        public const string FinalQuery = @"
            SELECT post.*,
                cte_Grouped.parent_id,
                cte_Grouped.r
            FROM post
            JOIN cte_Grouped ON post.internal_id = cte_Grouped.internal_id
            ORDER BY cte_Grouped.r ";

        public string BuildCustomPostQueryPaged(string filter)
        {
            return $@" WITH RECURSIVE cte_Posts AS
                    (
                        {ParentPostQuery}
                        {filter}
                        union
                        {ChildPostsQuery}
                    )
                    ,{GroupedPostsQuery}
                    {FinalQuery}
                    {Paged}; ";
        }

        public IEnumerable<PostTree> BuildResults(IEnumerable<PostPlus> posts, long id, string requestType, out QueryResponse response)
        {
            List<PostTree> result = new List<PostTree>();
            response = new QueryResponse();

            if (posts == null)
            {
                response.success = false;
                response.errorCode = "E0001";
                response.errorMessage = $@"Unable to retrieve {requestType} Thread with ID: {id}";
            }
            else
            {
                response.success = true;
                response.errorCode = string.Empty;
                response.errorMessage = string.Empty;
                if (posts.Any())
                {
                    var firstPost = posts.FirstOrDefault();

                    response.totalRows = firstPost.TotalRows ?? 0;

                    result.AddRange(BuildPostTreeList(posts));
                }
            }

            return result;
        }

        public IEnumerable<PostTree> GetPostsByAnchorId(long anchorId, int startRowIndex, int maximumRows, out QueryResponse response)
        {
            List<PostTree> result = new List<PostTree>();

            using (var db = _dbConnectionFactory.Open())
            {
                var query = BuildCustomPostQueryPaged(AnchorFilter);
                var posts = db.SqlList<PostPlus>(query, new { StartRowIndex = startRowIndex, MaximumRows = maximumRows, Id = anchorId });

                result.AddRange(BuildResults(posts, anchorId, "Anchor", out response));
            }
            
            return result;
        }

        public IEnumerable<PostTree> GetPostTree(long postId, int startRowIndex, int maximumRows, out QueryResponse response)
        {
            List<PostTree> result = new List<PostTree>();

            using (var db = _dbConnectionFactory.Open())
            {
                var query = BuildCustomPostQueryPaged(AnchorFilter);
                var posts = db.SqlList<PostPlus>(query, new { StartRowIndex = startRowIndex, MaximumRows = maximumRows, Id = postId });

                result.AddRange(BuildResults(posts, postId, "Post", out response));
            }

            return result;
        }

        public IEnumerable<PostTree> GetPostReferenceTree(long postId, int startRowIndex, int maximumRows, out QueryResponse response)
        {
            List<PostTree> result = new List<PostTree>();

            using (var db = _dbConnectionFactory.Open())
            {
                var query = BuildCustomPostQueryPaged(AnchorFilter);
                var posts = db.SqlList<PostPlus>(query, new { StartRowIndex = startRowIndex, MaximumRows = maximumRows, Id = postId });

                result.AddRange(BuildResults(posts, postId, "Post", out response));
            }

            return result;
        }

        public IEnumerable<PostTree> GetUserTree(long userId, int startRowIndex, int maximumRows, out QueryResponse response)
        {
            List<PostTree> result = new List<PostTree>();

            using (var db = _dbConnectionFactory.Open())
            {
                var query = BuildCustomPostQueryPaged(AnchorFilter);
                var posts = db.SqlList<PostPlus>(query, new { StartRowIndex = startRowIndex, MaximumRows = maximumRows, Id = userId });

                result.AddRange(BuildResults(posts, userId, "User", out response));
            }

            return result;
        }
        
        public IEnumerable<PostTree> GetUserReferenceTree(long userId, int startRowIndex, int maximumRows, out QueryResponse response)
        {
            List<PostTree> result = new List<PostTree>();

            using (var db = _dbConnectionFactory.Open())
            {
                var query = BuildCustomPostQueryPaged(UserReferenceFilter);
                var posts = db.SqlList<PostPlus>(query, new { StartRowIndex = startRowIndex, MaximumRows = maximumRows, Id = userId });

                result.AddRange(BuildResults(posts, userId, "User Reference", out response));
            }

            return result;
        }

        public IEnumerable<PostTree> BuildPostTreeList(IEnumerable<PostPlus> posts)
        {
            var result = new List<PostTree>();
            var tree = new Dictionary<long, PostTree>();

            foreach (var post in posts)
            {
                var current = new PostTree()
                {
                    Post = post
                };

                PostTree parent;
                if (post.ParentId.HasValue && tree.TryGetValue(post.ParentId.Value, out parent))
                {
                    parent.Children.Add(current);
                }
                else
                {
                    result.Add(current);
                }

                tree.Add(post.InternalId, current);
            }
            return result;
        }
    }
}
