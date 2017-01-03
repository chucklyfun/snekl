using ServiceStack.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snekl.Core.Domain
{
    public class PostTree
    {
        public Post Post { get; set; }
        
        public List<PostTree> Children { get; set; } = new List<PostTree>();
    }
}
