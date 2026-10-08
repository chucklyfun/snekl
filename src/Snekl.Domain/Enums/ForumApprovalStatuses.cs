namespace Snekl.Domain.Enums;

public static class ForumApprovalStatuses
{
    public const string Approved = "approved";
    public const string Pending = "pending";

    public static bool IsApproved(string? status) =>
        string.IsNullOrWhiteSpace(status) ||
        status.Equals(Approved, StringComparison.OrdinalIgnoreCase);
}
