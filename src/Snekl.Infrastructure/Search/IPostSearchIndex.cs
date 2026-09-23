namespace Snekl.Infrastructure.Search;

public interface IPostSearchIndex
{
    bool IsEnabled { get; }
    Task EnsureIndexAsync(CancellationToken ct = default);
    Task DeleteIndexAsync(CancellationToken ct = default);
    Task IndexAsync(PostSearchDocument document, CancellationToken ct = default);
    Task IndexManyAsync(IReadOnlyList<PostSearchDocument> documents, CancellationToken ct = default);
    Task DeleteAsync(Guid postId, CancellationToken ct = default);
    Task<long> CountAsync(CancellationToken ct = default);
    Task RefreshAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PostSearchMatch>> SearchAsync(
        string query,
        IReadOnlyList<Guid> forumIds,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
