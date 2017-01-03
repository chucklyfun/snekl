using ServiceStack.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snekl.Core.Domain
{
    public class Entity
    {
        [AutoIncrement] // Creates Auto primary key
        [PrimaryKey]
        public long InternalId { get; set; }
        
        public DateTime Updated { get; set; }
    }
}
