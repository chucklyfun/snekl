using Snekl.Core.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Snekl.Core.Repositories;
using Snekl.Core.Services;

namespace Snekl.Core.Controllers
{
    public interface IPostController : IEntityController<Post>
    {
        IEnumerable<PostTree> PostsByAnchorId(long id, int startRowIndex, int maximumRows, out QueryResponse response);

        IEnumerable<PostTree> PostTreeByUserId(long id, int startRowIndex, int maximumRows, out QueryResponse response);

        IEnumerable<PostTree> PostsByUserReferenceId(long id, int startRowIndex, int maximumRows, out QueryResponse response);

        IEnumerable<PostTree> PostTreeByPostId(long id, int startRowIndex, int maximumRows, out QueryResponse response);

        IEnumerable<PostTree> PostsByPostReferenceId(long id, int startRowIndex, int maximumRows, out QueryResponse response);
    }

    public class PostController : EntityController<Post>, IPostController
    {
        private IPostService _postService;

        public PostController(IEntityRepository<Post> entityRepository, IPostService postService) : base(entityRepository)
        {
            _postService = postService;
        }

        public IEnumerable<PostTree> PostsByAnchorId(long id, int startRowIndex, int maximumRows, out QueryResponse response)
        {
            return _postService.GetPostsByAnchorId(id, startRowIndex, maximumRows, out response);
        }

        public IEnumerable<PostTree> PostTreeByUserId(long id, int startRowIndex, int maximumRows, out QueryResponse response)
        {
            return _postService.GetUserTree(id, startRowIndex, maximumRows, out response);
        }

        public IEnumerable<PostTree> PostsByUserReferenceId(long id, int startRowIndex, int maximumRows, out QueryResponse response)
        {
            return _postService.GetUserReferenceTree(id, startRowIndex, maximumRows, out response);
        }

        public IEnumerable<PostTree> PostTreeByPostId(long id, int startRowIndex, int maximumRows, out QueryResponse response)
        {
            return _postService.GetPostTree(id, startRowIndex, maximumRows, out response);
        }

        public IEnumerable<PostTree> PostsByPostReferenceId(long id, int startRowIndex, int maximumRows, out QueryResponse response)
        {
            return _postService.GetPostReferenceTree(id, startRowIndex, maximumRows, out response);
        }
    }
}
