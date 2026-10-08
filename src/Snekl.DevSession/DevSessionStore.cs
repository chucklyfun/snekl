using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Snekl.DevSession;

public sealed class DevSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const int MaxLockAttempts = 40;
    private const int MinBackoffMs = 5;
    private const int MaxBackoffMs = 40;

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
            try
            {
                return WithLockedFile(stream =>
                {
                    var file = ReadFromStream(stream);
                    PruneDeadSessions(file);
                    return file;
                });
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
            WithLockedFile(stream =>
            {
                var directory = Path.GetDirectoryName(_filePath)!;
                var file = ReadFromStream(stream);

                if (string.IsNullOrWhiteSpace(file.RepoRoot))
                    file.RepoRoot = Path.GetFullPath(Path.Combine(directory, ".."));

                mutate(file);
                PruneDeadSessions(file);
                WriteToStream(stream, file);
            });
        }
    }

    private T WithLockedFile<T>(Func<FileStream, T> action)
    {
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);

        for (var attempt = 1; attempt <= MaxLockAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    _filePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.None);
                return action(stream);
            }
            catch (IOException) when (attempt < MaxLockAttempts)
            {
                Thread.Sleep(Random.Shared.Next(MinBackoffMs, MaxBackoffMs + 1));
            }
        }

        throw new IOException($"Timed out waiting for exclusive access to '{_filePath}'.");
    }

    private void WithLockedFile(Action<FileStream> action) =>
        WithLockedFile(stream =>
        {
            action(stream);
            return true;
        });

    private static DevSessionFile ReadFromStream(FileStream stream)
    {
        if (stream.Length == 0)
            return new DevSessionFile();

        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var json = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(json))
            return new DevSessionFile();

        try
        {
            return JsonSerializer.Deserialize<DevSessionFile>(json, JsonOptions) ?? new DevSessionFile();
        }
        catch
        {
            return new DevSessionFile();
        }
    }

    private static void WriteToStream(FileStream stream, DevSessionFile file)
    {
        var json = JsonSerializer.Serialize(file, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        stream.SetLength(0);
        stream.Position = 0;
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
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
