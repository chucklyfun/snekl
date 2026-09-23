namespace Snekl.Domain.Enums;

/// <summary>
/// Extensible grant strength for a single permission. Ordered for atLeast/below checks:
/// None &lt; Request &lt; Direct.
/// </summary>
public enum GrantMode
{
    None = 0,
    Request = 1,
    Direct = 2
}
