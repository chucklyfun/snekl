namespace Snekl.Commissions;

public class CommissionsOptions
{
    public const string SectionName = "Commissions";

    public string PublicBaseUrl { get; set; } = "http://localhost:5301";
    public string AuthBaseUrl { get; set; } = "http://localhost:5100";
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
    public string ClientId { get; set; } = "snekl-commissions";
    public string ClientSecret { get; set; } = "snekl-commissions-dev-secret-change-me";
    public string ConnectionString { get; set; } = "Data Source=App_Data/commissions.db";

    public string WebhookUrl => PublicBaseUrl.TrimEnd('/') + "/api/ccbill/webhook";
}
