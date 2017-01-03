using ServiceStack.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snekl.Core.Domain
{
    public class AnchorLink : Entity
    {
        [References(typeof(Post))]
        public long? PostId { get; set; }

        [References(typeof(Anchor))]
        public long? AnchorId { get; set; }

        [Reference]
        public Post Post { get; set; }

        [Reference]
        public Anchor Anchor { get; set; }
    }
}
