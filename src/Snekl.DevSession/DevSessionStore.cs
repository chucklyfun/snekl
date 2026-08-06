using System.Diagnostics;
using System.Text.Json;

namespace Snekl.DevSession;

public sealed class DevSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;
    private readonly object _gate = new();

    public DevSessionStore(string repoRoot)
    {
        _filePath = RepoPaths.DevSessionsFile(repoRoot);
    }

    public DevSessionFile Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
                return new DevSessionFile { RepoRoot = Path.GetDirectoryName(_filePath) is { } dir ? Path.GetFullPath(Path.Combine(dir, "..")) : "" };

            try
            {
                var json = File.ReadAllText(_filePath);
                var file = JsonSerializer.Deserialize<DevSessionFile>(json, JsonOptions) ?? new DevSessionFile();
                PruneDeadSessions(file);
                return file;
            }
            catch
            {
                return new DevSessionFile();
            }
        }
    }

    public void Register(int pid, string service, string? urls)
    {
        Update(file =>
        {
            file.Sessions.RemoveAll(s => s.Pid == pid);
            file.Sessions.Add(new DevSessionRecord
            {
                Pid = pid,
                Service = service,
                Urls = urls,
                StartedAt = DateTimeOffset.UtcNow
            });
        });
    }

    public void RecordRequest(int pid, string method, string path, string client)
    {
        Update(file =>
        {
            var session = file.Sessions.FirstOrDefault(s => s.Pid == pid);
            if (session is null)
            {
                session = new DevSessionRecord
                {
                    Pid = pid,
                    Service = "unknown",
                    StartedAt = DateTimeOffset.UtcNow
                };
                file.Sessions.Add(session);
            }

            var now = DateTimeOffset.UtcNow;
            session.LastRequestAt = now;
            session.LastRequestMethod = method;
            session.LastRequestPath = path;
            session.LastRequestClient = client;
            session.RequestCount++;

            if (string.Equals(client, "cursor-agent", StringComparison.OrdinalIgnoreCase))
                session.LastCursorRequestAt = now;
        });
    }

    public void Unregister(int pid)
    {
        Update(file => file.Sessions.RemoveAll(s => s.Pid == pid));
    }

    private void Update(Action<DevSessionFile> mutate)
    {
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(directory);

            var file = File.Exists(_filePath)
                ? JsonSerializer.Deserialize<DevSessionFile>(File.ReadAllText(_filePath), JsonOptions) ?? new DevSessionFile()
                : new DevSessionFile();

            if (string.IsNullOrWhiteSpace(file.RepoRoot))
                file.RepoRoot = Path.GetFullPath(Path.Combine(directory, ".."));

            mutate(file);
            PruneDeadSessions(file);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(file, JsonOptions));
        }
    }

    private static void PruneDeadSessions(DevSessionFile file)
    {
        file.Sessions.RemoveAll(s =>
        {
            try
            {
                var process = Process.GetProcessById(s.Pid);
                return process.HasExited;
            }
            catch
            {
                return true;
            }
        });
    }
}
