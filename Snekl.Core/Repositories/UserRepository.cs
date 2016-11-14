using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NReco.Data;


namespace Snekl.Core.Repositories
{
    public class UserRepository
    {
        private DbCommandBuilder _dbCommandBuilder;

        public UserRepository(DbCommandBuilder dbCommandBuilder)
        {
            _dbCommandBuilder = dbCommandBuilder;
        }

        public User FindById(Guid id)
        {
            var command = _dbCommandBuilder.GetSelectCommand(new Query("User", (QField)"Id" == new QConst(id)));

            command.
        }
    }
}
