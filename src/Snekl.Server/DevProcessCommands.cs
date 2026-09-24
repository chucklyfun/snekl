using System.Diagnostics;
using System.Text;
using Snekl.DevSession;

namespace Snekl.Server;

public static class DevProcessCommands
{
    /// <summary>Content, Auth, Hub, and Faker — anything that locks Debug output under src/.</summary>
    private static readonly string[] ServiceProcessNames =
    [
        "Snekl.Api",
        "Snekl.Account.Api",
        "Snekl.Hub",
        "Snekl.Faker",
    ];

    private static readonly int[] DevListenPorts = [5001, 5002, 5100, 5200, 5300];

    public static int RunStatus(string root)
    {
        var store = new DevSessionStore(root);
        var sessions = store.Load().Sessions.ToDictionary(s => s.Pid);
        var processes = FindRunningServices();

        Console.WriteLine("Snekl dev API processes:");
        Console.WriteLine();

        if (processes.Count == 0)
        {
            Console.WriteLine("  (none running)");
            return 0;
        }

        foreach (var process in processes.OrderBy(p => p.ProcessName).ThenBy(p => p.Id))
        {
            sessions.TryGetValue(process.Id, out var session);
            WriteProcessBlock(process, session);
            Console.WriteLine();
        }

        Console.WriteLine($"Stop when ready:  {CliHints.StopCommand}");
        return 0;
    }

    public static int RunStop(string root)
    {
        var store = new DevSessionStore(root);
        var sessions = store.Load().Sessions.ToDictionary(s => s.Pid);
        var processes = FindRunningServices();

        if (processes.Count == 0)
        {
            Console.WriteLine("No Snekl API processes are running.");
            return 0;
        }

        Console.WriteLine("Stopping Snekl API processes:");
        Console.WriteLine();

        foreach (var process in processes.OrderBy(p => p.ProcessName).ThenBy(p => p.Id))
        {
            sessions.TryGetValue(process.Id, out var session);
            WriteProcessBlock(process, session);
            Console.WriteLine();
        }

        var failures = 0;
        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                    store.Unregister(process.Id);
                    Console.WriteLine($"Stopped {process.ProcessName} (PID {process.Id}).");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"Failed to stop {process.ProcessName} (PID {process.Id}): {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        return failures > 0 ? 1 : 0;
    }

    private static List<Process> FindRunningServices()
    {
        var byId = new Dictionary<int, Process>();
        var selfPid = Environment.ProcessId;

        foreach (var name in ServiceProcessNames)
            AddMatchingProcesses(byId, name);

        // Orphan orchestrators from prior `snekl dev` sessions (never the current stop/status process).
        foreach (var process in Process.GetProcessesByName("snekl"))
        {
            if (process.Id == selfPid)
            {
                process.Dispose();
                continue;
            }

            try
            {
                var path = process.MainModule?.FileName;
                if (path is not null &&
                    path.Contains("Snekl", StringComparison.OrdinalIgnoreCase) &&
                    path.Contains("Snekl.Server", StringComparison.OrdinalIgnoreCase))
                {
                    byId[process.Id] = process;
                }
                else
                {
                    process.Dispose();
                }
            }
            catch
            {
                byId[process.Id] = process;
            }
        }

        // Anything still bound to the known API ports (covers rename / hostfxr edge cases).
        foreach (var pid in FindPidsListeningOnDevPorts())
            TryAddPid(byId, pid, selfPid);

        // Parent `dotnet run --project src/Snekl.*` hosts also lock Debug output.
        foreach (var pid in FindDotnetHostPids())
            TryAddPid(byId, pid, selfPid);

        return byId.Values.ToList();
    }

    private static void AddMatchingProcesses(Dictionary<int, Process> byId, string name)
    {
        foreach (var process in Process.GetProcessesByName(name))
        {
            if (byId.ContainsKey(process.Id))
            {
                process.Dispose();
                continue;
            }

            try
            {
                if (process.MainModule?.FileName?.Contains("Snekl", StringComparison.OrdinalIgnoreCase) == true)
                    byId[process.Id] = process;
                else
                    process.Dispose();
            }
            catch
            {
                byId[process.Id] = process;
            }
        }
    }

    private static void TryAddPid(Dictionary<int, Process> byId, int pid, int selfPid)
    {
        if (pid == selfPid || byId.ContainsKey(pid)) return;
        try
        {
            byId[pid] = Process.GetProcessById(pid);
        }
        catch
        {
            // Process may have exited between query and open.
        }
    }

    private static IEnumerable<int> FindDotnetHostPids()
    {
        const string command =
            @"Get-CimInstance Win32_Process -Filter ""Name = 'dotnet.exe'"" |
              Where-Object {
                $_.CommandLine -match 'Snekl\.(Api|Account\.Api|Hub|Faker|Server)' -and
                $_.CommandLine -notmatch '\sstop(\s|$)'
              } |
              Select-Object -ExpandProperty ProcessId";
        return QueryPidsViaPowerShell(command);
    }

    private static IEnumerable<int> FindPidsListeningOnDevPorts()
    {
        var ports = string.Join(',', DevListenPorts);
        return QueryPidsViaPowerShell(
            $"(Get-NetTCPConnection -LocalPort {ports} -State Listen -ErrorAction SilentlyContinue).OwningProcess | Select-Object -Unique");
    }

    private static IEnumerable<int> QueryPidsViaPowerShell(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                ArgumentList = { "-NoProfile", "-Command", command },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) return [];

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(8000);
            return ParsePidLines(output);
        }
        catch
        {
            return [];
        }
    }

    private static List<int> ParsePidLines(string output)
    {
        var pids = new List<int>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(line.Trim(), out var pid) && pid > 0)
                pids.Add(pid);
        }
        return pids;
    }

    private static void WriteProcessBlock(Process process, DevSessionRecord? session)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"  {process.ProcessName}  PID {process.Id}");

        try
        {
            if (process.MainModule?.FileName is { } path)
                builder.AppendLine($"    Path:         {path}");
        }
        catch
        {
            // Access denied for some processes.
        }

        if (session is not null)
        {
            if (!string.IsNullOrWhiteSpace(session.Urls))
                builder.AppendLine($"    URLs:         {session.Urls}");
            builder.AppendLine($"    Started:      {FormatTimestamp(session.StartedAt)} ({FormatAge(session.StartedAt)} ago)");

            if (session.LastRequestAt is { } lastRequest)
            {
                builder.AppendLine($"    Last request: {FormatTimestamp(lastRequest)} ({FormatAge(lastRequest)} ago)");
                builder.AppendLine($"                  {session.LastRequestMethod} {session.LastRequestPath}");
                builder.AppendLine($"                  client: {session.LastRequestClient ?? "unknown"}");
                builder.AppendLine($"    Requests:     {session.RequestCount}");
            }
            else
            {
                builder.AppendLine("    Last request: (none recorded)");
            }

            if (session.LastCursorRequestAt is { } lastCursor)
                builder.AppendLine($"    Last cursor:  {FormatTimestamp(lastCursor)} ({FormatAge(lastCursor)} ago)");
        }
        else
        {
            builder.AppendLine("    Activity:     (no session file — started before tracking or outside this repo)");
        }

        builder.AppendLine("    Note:         Locks Debug build output while running.");
        Console.Write(builder.ToString());
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private static string FormatAge(DateTimeOffset value)
    {
        var span = DateTimeOffset.UtcNow - value.ToUniversalTime();
        if (span.TotalSeconds < 60) return $"{Math.Max(0, (int)span.TotalSeconds)}s";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h";
        return $"{(int)span.TotalDays}d";
    }
}
