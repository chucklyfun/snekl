using ServiceStack;
using System.Linq;
using Snekl.Core.Controllers;
using ServiceStack.OrmLite;
using Snekl.Core.Domain;
using Funq;
using Utilities;
using ServiceStack.Data;

namespace Website.Startup
{
    public class IocLoader : IIocLoader
    {
        public Funq.Container RegisterModule(Container container)
        {
            container.Register<IDbConnectionFactory>(new OrmLiteConnectionFactory(
                System.Configuration.ConfigurationManager.ConnectionStrings["Test"].ConnectionString,
                PostgreSqlDialect.Provider));            

            return container;
        }
    }
}
