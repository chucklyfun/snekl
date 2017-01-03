using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using ServiceStack;
using Snekl.Core.Repositories;
using Snekl.Core.Domain;

namespace Snekl.Core.Controllers
{
    public interface IPublicEntityController<T> where T : PublicEntity
    {
        T Single(string id, out QueryResponse queryResponse);
    }

    public class PublicEntityController<T> : IPublicEntityController<T> where T : PublicEntity
    {
        private IPublicEntityRepository<T> _entityRepository;

        public PublicEntityController(IPublicEntityRepository<T> entityRepository)
        {
            _entityRepository = entityRepository;
        }

        public T Single(string id, out QueryResponse response)
        {
            T result = null;

            response = new QueryResponse();


            Guid guid = default(Guid);
            if (Guid.TryParse(id, out guid))
            {
                result = _entityRepository.GetByExternalId(guid);

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
            }
            else
            {
                response.success = false;
                result = default(T);
                response.errorCode = "E0002";
                response.errorMessage = $@"Invalid ID: {id}";
            }

            return result;
        }   
    }
}