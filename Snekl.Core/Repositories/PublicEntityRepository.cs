using System;
using System.Collections.Generic;
using System.Linq;
using Snekl.Core.Domain;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using System.Linq.Expressions;

namespace Snekl.Core.Repositories
{
    public interface IPublicEntityRepository<T> : IEntityRepository<T>
        where T : PublicEntity
    {
        T GetByExternalId(Guid externalId);
    }

    public class PublicEntityRepository<T> : IPublicEntityRepository<T>
        where T : PublicEntity
    {
        private IDbConnectionFactory _dbConnectionFactory;
        private IEntityRepository<T> _entityRepository;

        public PublicEntityRepository(IDbConnectionFactory dbConnectionFactory, IEntityRepository<T> entityRepository)
        {
            _dbConnectionFactory = dbConnectionFactory;
            _entityRepository = entityRepository;
        }

        public T GetByExternalId(Guid externalId)
        {
            T result = null;

            using (var db = _dbConnectionFactory.Open())
            {
                result = db.Single<T>(f => f.ExternalId == externalId);
            }

            return result;
        }

        public T GetByInternalId(long internalId)
        {
            return _entityRepository.GetByInternalId(internalId);
        }

        public T FirstOrDefault(Expression<Func<T, bool>> query = null)
        {
            return _entityRepository.FirstOrDefault(query);
        }

        public List<T> Select(Expression<Func<T, bool>> query = null)
        {
            return _entityRepository.Select(query);
        }

        public long Insert(T entity)
        {
            entity.ExternalId = Guid.NewGuid();

            var result = _entityRepository.Insert(entity);

            if (result <= 0)
            {
                entity.ExternalId = default(Guid);
            }

            return result;
        }

        public int Update(T entity)
        {
            return _entityRepository.Update(entity);
        }


        public int Upsert(T entity)
        {
            var newGuid = false;
            if (entity.ExternalId == default(Guid))
            {
                entity.ExternalId = Guid.NewGuid();
                newGuid = true;
            }

            var result = _entityRepository.Upsert(entity);

            if (result <= 0 && newGuid)
            {
                entity.ExternalId = default(Guid);
            }

            return result;
        }


        public int Delete(T entity)
        {
            return _entityRepository.Delete(entity);
        }
    }
}
