namespace Snekl.Domain.Entities;

public class User : ExternalEntity
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;

    public ICollection<Post> Posts { get; set; } = new List<Post>();
    public ICollection<Forum> Forums { get; set; } = new List<Forum>();
}
