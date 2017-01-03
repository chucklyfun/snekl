using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using ServiceStack;
using Snekl.Core.Domain;

namespace Website.ServiceModel
{
    [Route("/post/get/{id}")]
    public class PostIdRequest : IReturn<PostResponse>
    {
        public long Id { get; set; }
    }

    [Route("/post/user/{id}/{index}/{MaxEntries}")]
    public class TreeUserIdRequest : IReturn<TreeListResponse>
    {
        public long Id { get; set; } = 0;

        public int Index { get; set; } = 0;

        public int MaxEntries { get; set; } = 0;
    }

    [Route("/post/tree/{id}/{index}/{MaxEntries}")]
    public class TreePostIdRequest : IReturn<TreeListResponse>
    {
        public long Id { get; set; } = 0;

        public int Index { get; set; } = 0;

        public int MaxEntries { get; set; } = 0;
    }

    [Route("/post/anchor/references/{id}")]
    public class ReferencesAnchorIdRequest : IReturn<TreeListResponse>
    {
        public long Id { get; set; } = 0;

        public int Index { get; set; } = 0;

        public int MaxEntries { get; set; } = 0;
    }


    [Route("/post/anchor/references/{id}")]
    public class ReferencesUserIdRequest : IReturn<TreeListResponse>
    {
        public long Id { get; set; } = 0;

        public int Index { get; set; } = 0;

        public int MaxEntries { get; set; } = 0;
    }


    [Route("/post/references/{id}")]
    public class ReferencesPostIdRequest : IReturn<TreeListResponse>
    {
        public long Id { get; set; } = 0;

        public int Index { get; set; } = 0;

        public int MaxEntries { get; set; } = 0;
    }

    public class PostResponse : IMeta
    {
        public Dictionary<string, string> Meta { get; set; } = new Dictionary<string, string>();
        public Post Result { get; set; }
    }

    public class TreeListResponse : IMeta
    {
        public Dictionary<string, string> Meta { get; set; } = new Dictionary<string, string>();
        public List<PostTree> Result { get; set; } = new List<PostTree>();

        public int TotalRows { get; set; } = 0;
    }
}