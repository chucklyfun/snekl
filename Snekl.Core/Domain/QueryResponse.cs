using ServiceStack.DataAnnotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snekl.Core.Domain
{
    public class QueryResponse
    {
        public int totalRows { get; set; } = 0;
        public bool success { get; set; } = false;
        public string errorCode { get; set; } = string.Empty;
        public string errorMessage { get; set; } = string.Empty;
    }
}
