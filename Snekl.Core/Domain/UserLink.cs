using ServiceStack.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snekl.Core.Domain
{
    public class UserLink : Entity
    {
        [References(typeof(Post))]
        public long? PostId { get; set; }

        [References(typeof(User))]
        public long? UserId { get; set; }

        [Reference]
        public Post Post { get; set; }

        [Reference]
        public User User { get; set; }
    }
}
