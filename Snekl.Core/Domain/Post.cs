using ServiceStack.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snekl.Core.Domain
{
    public class Post : Entity
    {
        public string Message { get; set; } = string.Empty;

        [References(typeof(User))]
        public long? UserId { get; set; }

        [Reference]
        public User User { get; set; }

        [References(typeof(Post))]
        public long? ParentId { get; set; }

        [Reference]
        public Post Parent { get; set; }

        [Reference]
        public List<PostLink> Posts { get; set; }

        [Reference]
        public List<AnchorLink> Anchors { get; set; }
        
        [Reference]
        public List<User> UserReferences { get; set; }
    }
}
