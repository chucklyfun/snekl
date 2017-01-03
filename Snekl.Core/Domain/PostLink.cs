using ServiceStack.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snekl.Core.Domain
{
    public class PostLink : Entity
    {
        [References(typeof(Post))]
        public long? ReferencePostId { get; set; }

        [References(typeof(Post))]
        public long? SourcePostId { get; set; }

        [Reference]
        public Post ReferencePost { get; set; }

        [Reference]
        public Post SourcePost { get; set; }
    }
}
