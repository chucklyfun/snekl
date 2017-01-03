using ServiceStack.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snekl.Core.Domain
{
    public class PostPlus : Post
    {
        public int? Rank { get; set; }

        public int? TotalRows { get; set; }
    }
}
