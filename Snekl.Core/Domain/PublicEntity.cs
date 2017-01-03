using ServiceStack.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snekl.Core.Domain
{
    public class PublicEntity : Entity 
    {
        [Index(Unique = true)]
        public Guid ExternalId { get; set; }
    }
}
