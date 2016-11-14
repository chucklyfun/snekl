using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Snekl.Core.Domain;

namespace Snekl.Server.Controllers.User
{
    public interface IUserController
    {
        Snekl.Core.Domain.User FindById(dynamic input);
    }

    public class UserController : IUserController
    {
        public Snekl.Core.Domain.User FindById(dynamic input)
        {
            // Database lookup by input.id

            throw new NotImplementedException();
        }
    }
}
