using System;
using System.Collections.Generic;
using System.Linq;
using ServiceStack;
using Website.ServiceModel;
using Snekl.Core.Domain;
using Snekl.Core.Controllers;

namespace Website.ServiceInterface
{
    public interface IUserController
    {
        object Any(UserIdRequest request);
    }

    public class UserController : Service, IUserController
    {
        private IPublicEntityController<User> _entityController;

        public UserController(IPublicEntityController<User> entityRepository)
        {
            _entityController = entityRepository;
        }

        public object Any(UserIdRequest request)
        {
            IMeta response = null;
            User user = null;
            QueryResponse queryResponse = null;

            user = _entityController.Single(request.Id, out queryResponse);

            if (queryResponse.success)
            {
                response = new UserResponse()
                {
                    Result = user
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