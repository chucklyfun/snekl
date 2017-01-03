using System;
using NUnit.Framework;
using ServiceStack;
using ServiceStack.Testing;
using Website.ServiceModel;
using Website.ServiceInterface;
using Snekl.Core.Domain;
using System.Linq;
using Snekl.Core.Repositories;
using ServiceStack.Data;
using ServiceStack.OrmLite;
using AutoMoq;
using Moq;
using Snekl.Core.Controllers;

namespace Website.Tests
{
    [TestFixture]
    public class UnitTests
    {
        public UnitTests()
        {
            
        }

        [TestFixtureTearDown]
        public void TestFixtureTearDown()
        {
        }

        [Test]
        public void TestMethod1()
        {
            // Arrange
            var mocker = new AutoMoqer();
            var service = mocker.Create<UserController>();
            var controllerMock = mocker.GetMock<IPublicEntityController<User>>();
            var user = new User()
            {
                Email = "bartholomew@geemail.com"
            };

            var queryResponse = new QueryResponse()
            {
                errorCode = "NoErrorCode",
                errorMessage = "NoErrorMessage",
                success = false
            };

            controllerMock.Setup(f => f.Single(It.IsAny<string>(), out queryResponse)).Returns(user);



            // Act
            var response = (UserResponse)service.Any(new UserIdRequest { Id = "12345" });



            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Result.Email, Is.EqualTo("bartholomew@geemail.com"));
        }
    }
}
