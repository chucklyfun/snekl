using Funq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utilities
{
    public interface IIocLoader
    {
        Container RegisterModule(Container container);
    }
}
