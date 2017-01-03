using System;
using System.Collections.Generic;
using System.Linq;
using Snekl.Core.Domain;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using System.Linq.Expressions;

namespace Snekl.Core.Repositories
{
    public interface IEntityRepository<T> where T : Entity
    {
        T GetByInternalId(long internalId);

        T FirstOrDefault(Expression<Func<T, bool>> query = null);

        List<T> Select(Expression<Func<T, bool>> query = null);

        long Insert(T entity);

        int Update(T entity);

        int Upsert(T entity);

        int Delete(T entity);
    }

    public class EntityRepository<T> : IEntityRepository<T>
        where T : Entity
    {
        private IDbConnectionFactory _dbConnectionFactory;

        public EntityRepository(IDbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public T GetByInternalId(long internalId)
        {
            T result = null;

            using (var db = _dbConnectionFactory.Open())
            {
                result = db.SingleById<T>(internalId);
            }

            return result;
        }

        public T FirstOrDefault(Expression<Func<T, bool>> query = null)
        {
            T result = null;

            using (var db = _dbConnectionFactory.Open())
            {
                result = db.Single<T>(query);
            }

            return result;
        }

        public List<T> Select(Expression<Func<T, bool>> query = null)
        {
            List<T> result = null;

           

            using (var db = _dbConnectionFactory.Open())
            {
                if (query == null)
                {
                    result = db.Select<T>();
                }
                else
                {
                    result = db.Select<T>(query);
                }
            }

            return result;
        }

        public long Insert(T entity)
        {
            long result = 0;

            using (var db = _dbConnectionFactory.Open())
            {
                entity.Updated = DateTime.UtcNow;
                result = db.Insert<T>(entity, true);
            }

            return result;
        }

        public int Update(T entity)
        {
            int result = 0;

            using (var db = _dbConnectionFactory.Open())
            {
                entity.Updated = DateTime.UtcNow;
                result = db.Update<T>(entity);
            }

            return result;
        }


        public int Upsert(T entity)
        {
            int result = 0;

            using (var db = _dbConnectionFactory.Open())
            {
                entity.Updated = DateTime.UtcNow;
                db.Save<T>(entity);
            }

            return result;
        }


        public int Delete(T entity)
        {
            int result = 0;

            using (var db = _dbConnectionFactory.Open())
            {
                result = db.Delete<T>(entity);
            }

            return result;
        }
    }
}
