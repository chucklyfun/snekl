using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Snekl.Core.Domain;
using ServiceStack.OrmLite;
using Snekl.Core.Repositories;

namespace Snekl.Core.Tests
{
    public class EntityRepositoryTests
    {
        [Test]
        //[Explicit]
        public void TestCreateDbTables()
        {
            var dbFactory = new OrmLiteConnectionFactory(
                System.Configuration.ConfigurationManager.ConnectionStrings["Test"].ConnectionString,
                MySqlDialect.Provider);

            var userRepository = new EntityRepository<User>(dbFactory);
            var anchorRepository = new EntityRepository<Anchor>(dbFactory);
            var postRepository = new EntityRepository<Post>(dbFactory);

            var anchorLinkRepository = new EntityRepository<AnchorLink>(dbFactory);
            var userLinkRepository = new EntityRepository<UserLink>(dbFactory);
            var postLinkRepository = new EntityRepository<PostLink>(dbFactory);

            var random = new Random();

            using (var db = dbFactory.Open())
            {
                if (db.CreateTableIfNotExists<User>())
                {
                    Assert.Greater(userRepository.Insert(new User() { Email = $@"test{random.Next()}@geemail.com" }), 0);
                }

                if (db.CreateTableIfNotExists<Anchor>())
                {
                    Assert.Greater(anchorRepository.Insert(new Anchor() { Name = $@"#TestAnchor.{random.Next()}", UserId = 1 }), 0);
                }

                if (db.CreateTableIfNotExists<Post>())
                {
                    Assert.Greater(postRepository.Insert(new Post() { Message = $@"TEST MESSAGE {random.Next()}", UserId = 1 }), 0);
                }

                if (db.CreateTableIfNotExists<UserLink>())
                {
                    Assert.Greater(userLinkRepository.Insert(new UserLink() { PostId = 1, UserId = 1 }), 0);
                }
                if (db.CreateTableIfNotExists<AnchorLink>())
                {
                    Assert.Greater(anchorLinkRepository.Insert(new AnchorLink() { PostId = 1, AnchorId = 1 }), 0);
                }
                if (db.CreateTableIfNotExists<PostLink>())
                {
                    Assert.Greater(postRepository.Insert(new Post() { Message = $@"TEST MESSAGE {random.Next()}", UserId = 1 }), 0);
                    Assert.Greater(postLinkRepository.Insert(new PostLink() { ParentPostId = 1, ChildPostId = 2 }), 0);
                }
            }
        }
        
        [Test]
        //[Explicit]
        public void TestInsert()
        {
            var dbFactory = new OrmLiteConnectionFactory(
                System.Configuration.ConfigurationManager.ConnectionStrings["Test"].ConnectionString,
                MySqlDialect.Provider);

            var entityRepository = new EntityRepository<User>(dbFactory);

            var random = new Random();


            System.Console.WriteLine($@"New USER ID: {entityRepository.Insert(new User() { Email = $@"test{random.Next()}@geemail.com" })}");

        }

        [Test]
        //[Explicit]
        public void GetUserData()
        {
            var dbFactory = new OrmLiteConnectionFactory(
                System.Configuration.ConfigurationManager.ConnectionStrings["Test"].ConnectionString,
                MySqlDialect.Provider);

            var entityRepository = new EntityRepository<User>(dbFactory);
            
            var result = entityRepository.Select().ToList();

            System.Console.WriteLine(string.Join(";", result.Select(f => f.InternalId)));
        }

        [Test]
        //[Explicit]
        public void GetUserDataFullJoin()
        {
            var dbFactory = new OrmLiteConnectionFactory(
                System.Configuration.ConfigurationManager.ConnectionStrings["Test"].ConnectionString,
                MySqlDialect.Provider);

            var entityRepository = new EntityRepository<User>(dbFactory);

            var result = entityRepository.Select().ToList();

            System.Console.WriteLine(string.Join(";", result.Select(f => f.InternalId)));
        }
    }
}
