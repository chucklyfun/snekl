using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using ServiceStack;
using Snekl.Core.Repositories;
using Snekl.Core.Domain;

namespace Snekl.Core.Controllers
{
    public interface IEntityController<T> where T : Entity
    {
        T Single(long id, out QueryResponse response);
    }

    public class EntityController<T> : IEntityController<T> where T : Entity
    {
        private IEntityRepository<T> _entityRepository;

        public EntityController(IEntityRepository<T> entityRepository)
        {
            _entityRepository = entityRepository;
        }

        public T Single(long id, out QueryResponse response)
        { 
            var result = _entityRepository.GetByInternalId(id);
            response = new QueryResponse();

            if (result != null)
            {
                response.success = true;
                response.errorCode = string.Empty;
                response.errorMessage = string.Empty;
            }
            else
            {
                response.success = false;
                response.errorCode = "E0001";
                response.errorMessage = $@"Unable to retrieve {typeof(T).ToString()} with ID: " + id;
            }

            return result;
        }   
    }
}