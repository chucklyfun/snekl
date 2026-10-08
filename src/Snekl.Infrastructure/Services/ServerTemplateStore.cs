using System.Text.Json;
using System.Text.Json.Serialization;
using Snekl.Domain.Enums;

namespace Snekl.Infrastructure.Services;

public interface IServerTemplateStore
{
    ServerTemplateDocument Read();

    void Write(ServerTemplateDocument document);
}

public sealed class ServerTemplateDocument
{
    public List<ServerTemplateIssuer> AuthIssuers { get; set; } = [];

    /// <summary>
    /// Spawned servers also trust the host's active auth issuers, read live, so later changes to
    /// the host's list reach them. AuthIssuers are extra issuers of the server's own.
    /// </summary>
    public bool InheritHostAuthIssuers { get; set; } = true;

    /// <summary>Spawned servers also use the host's accepted hubs, read live. Hub adds one of their own.</summary>
    public bool InheritHostHubs { get; set; } = true;

    public ServerTemplateHub Hub { get; set; } = new();

    public List<ServerTemplateRole> Roles { get; set; } = [];
}

public sealed class ServerTemplateIssuer
{
    public string Name { get; set; } = "";

    public string IssuerUrl { get; set; } = "";

    public string JwksUrl { get; set; } = "";
}

public sealed class ServerTemplateHub
{
    public string Name { get; set; } = "";

    public string BaseUrl { get; set; } = "";
}

public sealed class ServerTemplateRole
{
    public string Name { get; set; } = "";

    public string? Color { get; set; }

    public int Position { get; set; }

    public bool IsEveryone { get; set; }

    public Dictionary<string, GrantMode> Modes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ServerTemplateStore : IServerTemplateStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public ServerTemplateStore(string path) => _path = path;

    public ServerTemplateDocument Read()
    {
        if (!File.Exists(_path))
        {
            var created = Default();
            Write(created);
            return created;
        }

        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<ServerTemplateDocument>(json, JsonOptions) ?? Default();
    }

    public void Write(ServerTemplateDocument document)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(document, JsonOptions));
    }

    public static ServerTemplateDocument Default() => new()
    {
        Roles =
        [
            new ServerTemplateRole
            {
                Name = "@everyone",
                Position = 0,
                IsEveryone = true,
                Modes = PermissionModeMap.FromServerFlags(ServerPermissions.ViewServer)
            },
            new ServerTemplateRole
            {
                Name = "Member",
                Position = 1,
                Color = "#4f8cff",
                Modes = PermissionModeMap.FromServerFlags(
                    ServerPermissions.ViewServer | ServerPermissions.CreateForum | ServerPermissions.CreateWatercooler)
            },
            new ServerTemplateRole
            {
                Name = "Moderator",
                Position = 2,
                Color = "#43b581",
                Modes = PermissionModeMap.FromServerFlags(
                    ServerPermissions.ViewServer | ServerPermissions.CreateForum |
                    ServerPermissions.CreateWatercooler | ServerPermissions.ManageServerMembers |
                    ServerPermissions.ManageContentBlocks | ServerPermissions.ViewHistory)
            }
        ]
    };
}
