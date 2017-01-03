using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using ServiceStack;
using Website.ServiceModel;
using Snekl.Core.Repositories;
using Snekl.Core.Domain;
using Snekl.Core.Services;

namespace Website.ServiceInterface
{
    public interface IPostController
    {
        object Single(PostIdRequest request);

        object Any(ReferencesAnchorIdRequest request);

        object Any(ReferencesUserIdRequest request);

        object Any(ReferencesPostIdRequest request);

        object Any(TreePostIdRequest request);

        object Any(TreeUserIdRequest request);
    }

    public class PostController : Service, IPostController
    {
        private Snekl.Core.Controllers.IPostController _postControllerCore;

        public PostController(Snekl.Core.Controllers.IPostController postControllerCore)
        {
            _postControllerCore = postControllerCore;
        }

        public object Single(PostIdRequest request)
        {
            IMeta response = null;
            QueryResponse queryResponse;

            var post = _postControllerCore.Single(request.Id, out queryResponse);

            if (queryResponse.success)
            {
                response = new PostResponse()
                {
                    Result = post
                };
            }
            else
            {
                response = new ResponseError()
                {
                    ErrorCode = queryResponse.errorCode,
                    Message = queryResponse.errorMessage
                };
            }

            return response;
        }

        public object Any(ReferencesAnchorIdRequest request)
        {
            IMeta response = null;
            QueryResponse queryResponse = null;

            var tree = _postControllerCore.PostsByAnchorId(request.Id, request.Index, request.MaxEntries, out queryResponse);

            if (queryResponse.success)
            {
                response = new TreeListResponse()
                {
                    Result = tree.ToList(),
                    
                };
            }
            else
            {
                response = new ResponseError()
                {
                    ErrorCode = queryResponse.errorCode,
                    Message = queryResponse.errorMessage
                };
            }

            return response;
        }

        public object Any(ReferencesPostIdRequest request)
        {
            IMeta response = null;
            QueryResponse queryResponse = null;

            var tree = _postControllerCore.PostsByAnchorId(request.Id, request.Index, request.MaxEntries, out queryResponse);

            if (queryResponse.success)
            {
                response = new TreeListResponse()
                {
                    Result = tree.ToList(),

                };
            }
            else
            {
                response = new ResponseError()
                {
                    ErrorCode = queryResponse.errorCode,
                    Message = queryResponse.errorMessage
                };
            }

            return response;
        }

        public object Any(ReferencesUserIdRequest request)
        {
            IMeta response = null;
            QueryResponse queryResponse = null;

            var tree = _postControllerCore.PostsByAnchorId(request.Id, request.Index, request.MaxEntries, out queryResponse);

            if (queryResponse.success)
            {
                response = new TreeListResponse()
                {
                    Result = tree.ToList(),

                };
            }
            else
            {
                response = new ResponseError()
                {
                    ErrorCode = queryResponse.errorCode,
                    Message = queryResponse.errorMessage
                };
            }

            return response;
        }

        public object Any(TreePostIdRequest request)
        {
            IMeta response = null;
            QueryResponse queryResponse = null;

            var tree = _postControllerCore.PostTreeByPostId(request.Id, request.Index, request.MaxEntries, out queryResponse);

            if (queryResponse.success)
            {
                response = new TreeListResponse()
                {
                    Result = tree.ToList(),

                };
            }
            else
            {
                response = new ResponseError()
                {
                    ErrorCode = queryResponse.errorCode,
                    Message = queryResponse.errorMessage
                };
            }

            return response;
        }

        public object Any(TreeUserIdRequest request)
        {
            IMeta response = null;
            QueryResponse queryResponse = null;

            var tree = _postControllerCore.PostTreeByUserId(request.Id, request.Index, request.MaxEntries, out queryResponse);

            if (queryResponse.success)
            {
                response = new TreeListResponse()
                {
                    Result = tree.ToList(),

                };
            }
            else
            {
                response = new ResponseError()
                {
                    ErrorCode = queryResponse.errorCode,
                    Message = queryResponse.errorMessage
                };
            }

            return response;
        }
    }
}