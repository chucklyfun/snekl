using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nancy;
using Snekl.Server.Controllers.User;

namespace SneklConsole.Controllers.User
{
    public class UserController : NancyModule
    {
        public UserController(IUserController userController)
        {
            Get["/user/{id}"] = _ => userController.FindById(_);
        }
    }
}
