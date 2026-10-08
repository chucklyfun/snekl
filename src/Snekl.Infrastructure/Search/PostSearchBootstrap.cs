using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Snekl.Infrastructure.Data;

namespace Snekl.Infrastructure.Search;

public sealed class PostSearchBootstrap : IHostedService
{
    private readonly IPostSearchIndex _index;
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<PostSearchBootstrap> _logger;

    public PostSearchBootstrap(
        IPostSearchIndex index,
        IServiceScopeFactory scopes,
        IConfiguration config,
        ILogger<PostSearchBootstrap> logger)
    {
        _index = index;
        _scopes = scopes;
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_index.IsEnabled) return;

        try
        {
            var resetDb = _config.GetValue("Snekl:ResetDb", false);
            if (resetDb)
                await _index.DeleteIndexAsync(cancellationToken);

            await _index.EnsureIndexAsync(cancellationToken);
            var count = await _index.CountAsync(cancellationToken);
            if (!resetDb && count > 0) return;

            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SneklDbContext>();
            var posts = await db.Posts.AsNoTracking().Include(p => p.Author).ToListAsync(cancellationToken);
            if (posts.Count == 0) return;

            var byId = posts.ToDictionary(p => p.Id);
            Guid RootOf(Guid id)
            {
                var current = id;
                var guard = 0;
                while (byId.TryGetValue(current, out var post) && post.ParentPostId.HasValue && guard++ < 64)
                    current = post.ParentPostId.Value;
                return current;
            }

            var documents = posts
                .Select(p => PostSearchDocuments.From(p, RootOf(p.Id), p.Author?.Username ?? ""))
                .ToList();

            const int batch = 250;
            for (var i = 0; i < documents.Count; i += batch)
                await _index.IndexManyAsync(documents.Skip(i).Take(batch).ToList(), cancellationToken);

            await _index.RefreshAsync(cancellationToken);
            _logger.LogInformation("Indexed {Count} posts into OpenSearch", documents.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenSearch bootstrap failed; search will fall back to Postgres");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
