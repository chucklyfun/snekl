using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using ServiceStack;
using Snekl.Core.Domain;

namespace Website.ServiceModel
{
    [Route("/user/get/{id}")]
    public class UserIdRequest : IReturn<UserResponse>
    {
        public string Id { get; set; }
    }

    [Route("/user/email/{id}")]
    public class UserEmailRequest : IReturn<UserResponse>
    {
        public string Email { get; set; }
    }

    public class UserResponse : IMeta
    {
        public Dictionary<string, string> Meta { get; set; } = new Dictionary<string, string>();
        public User Result { get; set; }
    }
}