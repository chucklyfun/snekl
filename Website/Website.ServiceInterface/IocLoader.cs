using ServiceStack;
using System.Linq;
using Snekl.Core.Controllers;
using ServiceStack.OrmLite;
using Snekl.Core.Domain;
using Funq;
using Utilities;

namespace Website.ServiceInterface
{
    public class IocLoader : IIocLoader
    {
        public Funq.Container RegisterModule(Container container)
        {
            container.RegisterAs<PostController, IPostController>();
            container.RegisterAs<UserController, IUserController>();
            

            return container;
        }
    }
}
