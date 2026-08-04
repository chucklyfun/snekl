using System.Diagnostics;
using System.Text;
using Snekl.DevSession;

namespace Snekl.Server;

public static class DevProcessCommands
{
    private static readonly string[] ServiceProcessNames = ["Snekl.Api", "Snekl.Account.Api"];

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

        Console.WriteLine("Stop when ready:");
        Console.WriteLine("  dotnet run --project src/Snekl.Server --no-launch-profile -- stop --force");
        return 0;
    }

    public static int RunStop(string root, bool force)
    {
        var store = new DevSessionStore(root);
        var sessions = store.Load().Sessions.ToDictionary(s => s.Pid);
        var processes = FindRunningServices();

        if (processes.Count == 0)
        {
            Console.WriteLine("No Snekl API processes are running.");
            return 0;
        }

        Console.WriteLine(force ? "Stopping Snekl API processes:" : "Snekl API processes (use --force to stop):");
        Console.WriteLine();

        foreach (var process in processes.OrderBy(p => p.ProcessName).ThenBy(p => p.Id))
        {
            sessions.TryGetValue(process.Id, out var session);
            WriteProcessBlock(process, session);
            Console.WriteLine();
        }

        if (!force)
        {
            Console.WriteLine("Review activity above. To stop all listed processes:");
            Console.WriteLine("  dotnet run --project src/Snekl.Server --no-launch-profile -- stop --force");
            return 0;
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
        var results = new List<Process>();
        foreach (var name in ServiceProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    if (process.MainModule?.FileName?.Contains("Snekl", StringComparison.OrdinalIgnoreCase) == true)
                        results.Add(process);
                    else
                        process.Dispose();
                }
                catch
                {
                    results.Add(process);
                }
            }
        }

        return results;
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
