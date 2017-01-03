using ServiceStack;
using Snekl.Core.Repositories;
using System.Linq;
using Snekl.Core.Controllers;
using ServiceStack.OrmLite;
using Snekl.Core.Domain;
using Funq;
using Utilities;
using Snekl.Core.Services;

namespace Snekl.Core
{
    public class IocLoader : IIocLoader
    {
        public Funq.Container RegisterModule(Container container)
        {
            container.RegisterAs<PublicEntityController<User>, IPublicEntityController<User>>();
            container.RegisterAs<PublicEntityRepository<User>, IPublicEntityRepository<User>>();
            container.RegisterAs<EntityRepository<Post>, IEntityRepository<Post>>();
            container.RegisterAs<EntityController<Post>, IEntityController<Post>>();
            container.RegisterAs<PostService, IPostService>();
            container.RegisterAs<PostController, IPostController>();

            //GetType().Assembly.GetTypes()
            //    .Where(x => x.IsOrHasGenericInterfaceTypeOf(typeof(IEntityRepository<>)))
            //    .Each(x => container.RegisterAutoWiredType(x));
            //GetType().Assembly.GetTypes()
            //    .Where(x => x.IsOrHasGenericInterfaceTypeOf(typeof(IPublicEntityRepository<>)))
            //    .Each(x => container.RegisterAutoWiredType(x));
            //GetType().Assembly.GetTypes()
            //    .Where(x => x.IsOrHasGenericInterfaceTypeOf(typeof(IPublicEntityController<>)))
            //    .Each(x => container.RegisterAutoWiredType(x));

            return container;
        }
    }
}
