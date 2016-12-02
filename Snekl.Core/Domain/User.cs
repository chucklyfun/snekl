using ServiceStack.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snekl.Core.Domain
{
    public class User : PublicEntity
    {
        [Index(Unique = true)]
        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string Salt { get; set; } = string.Empty;

        public Dictionary<string, string> NameParts { get; set; } = new Dictionary<string, string>();

        [Reference]
        public List<Post> Posts { get; set; }

        [Reference]
        public List<Post> Anchors { get; set; }
    }
}
