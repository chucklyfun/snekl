namespace Snekl.Infrastructure.Search;

public sealed class NullPostSearchIndex : IPostSearchIndex
{
    public bool IsEnabled => false;

    public Task EnsureIndexAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task DeleteIndexAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task IndexAsync(PostSearchDocument document, CancellationToken ct = default) => Task.CompletedTask;

    public Task IndexManyAsync(IReadOnlyList<PostSearchDocument> documents, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task DeleteAsync(Guid postId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<long> CountAsync(CancellationToken ct = default) => Task.FromResult(0L);

    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<PostSearchMatch>> SearchAsync(
        string query,
        IReadOnlyList<Guid> forumIds,
        int page,
        int pageSize,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PostSearchMatch>>([]);
}
