using ServiceStack.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snekl.Core.Domain
{
    public class Anchor : Entity
    {
        public string Name { get; set; } = string.Empty;
        
        [References(typeof(User))]
        public long? UserId { get; set; }
        
        [Reference]
        public User User { get; set; }

        [Reference]
        public List<AnchorLink> PostLinks { get; set; }
    }
}
