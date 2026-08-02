using System.Diagnostics;
using System.Net.Sockets;

namespace Snekl.Server;

public static class RepoPaths
{
    public static string FindRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Snekl.Modern.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (Snekl.Modern.sln).");
    }
}

public sealed class ProcessRunner : IDisposable
{
    private readonly List<Process> _processes = [];

    public Process Start(string fileName, string arguments, string workingDirectory, IDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(e.Data);
            Console.ResetColor();
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _processes.Add(process);
        return process;
    }

    public static int RunToCompletion(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    public void Dispose()
    {
        foreach (var process in _processes.ToList())
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
            }
            catch
            {
                // Best effort shutdown.
            }
            finally
            {
                process.Dispose();
            }
        }

        _processes.Clear();
    }
}

public static class StackOrchestrator
{
    private const string ContentUrl = "http://localhost:5001";
    private const string AuthUrl = "http://localhost:5100";
    private const string FrontendUrl = "http://localhost:5173";

    public static async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        var command = ResolveCommand(args);
        var root = RepoPaths.FindRoot(Directory.GetCurrentDirectory());
        var withFrontend = !args.Contains("--no-frontend");

        Console.WriteLine($"Snekl stack: running '{command}' (args: {(args.Length == 0 ? "(none — use -- up or --launch-profile up)" : string.Join(' ', args))})");

        return command switch
        {
            "dev" => await RunDevAsync(root, withFrontend, ct),
            "up" => RunDockerCompose(root, "docker-compose.yml", "up", "--build"),
            "down" => RunDockerCompose(root, "docker-compose.yml", "down"),
            "deps" => RunDockerCompose(root, "docker-compose.deps.yml", "up", "-d"),
            "deps-down" => RunDockerCompose(root, "docker-compose.deps.yml", "down"),
            "help" or "--help" or "-h" => PrintHelp(),
            _ => UnknownCommand(command)
        };
    }

    private static string ResolveCommand(string[] args)
    {
        if (args.Length == 0)
            return "dev";

        foreach (var arg in args)
        {
            if (arg.StartsWith('-'))
                continue;

            return arg.ToLowerInvariant();
        }

        return "dev";
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        return PrintHelp();
    }

    private static int PrintHelp()
    {
        Console.WriteLine("""
            Snekl stack orchestrator

            Usage (pick one style):
              dotnet run --project src/Snekl.Server --launch-profile up
              dotnet run --project src/Snekl.Server -- up

            Note: Without "--" before the command, dotnet may not pass "up" to this app
            and you'll get "dev" instead. Prefer --launch-profile on Windows.

            Commands:
              dev         Start Redis + Postgres (Docker), then auth/content APIs locally
              up          Start the full Docker stack (docker compose up --build)
              down        Stop the full Docker stack
              deps        Start only Redis + Postgres for local development
              deps-down   Stop Redis + Postgres
              help        Show this help

            Launch profiles (same commands, no "--" needed):
              --launch-profile dev | up | down | deps | deps-down

            Options:
              --no-frontend   Skip starting the React dev server during 'dev'
            """);
        return 0;
    }

    private static int RunDockerCompose(string root, string composeFile, params string[] composeArgs)
    {
        if (!IsDockerAvailable())
        {
            Console.Error.WriteLine("""
                Docker is not available on PATH. The 'up', 'down', and 'deps' commands require Docker Desktop.

                Install/start Docker Desktop, then retry:
                  dotnet run --project src/Snekl.Server --launch-profile up

                For local APIs without the full Docker stack, use:
                  dotnet run --project src/Snekl.Server --launch-profile dev
                """);
            return 1;
        }

        var args = string.Join(' ', composeArgs.Prepend("-f").Prepend(composeFile));
        Console.WriteLine($"Running: docker compose {args}");
        return ProcessRunner.RunToCompletion("docker", $"compose {args}", root);
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            return ProcessRunner.RunToCompletion("docker", "version --format '{{.Server.Version}}'", Directory.GetCurrentDirectory()) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int> RunDevAsync(string root, bool withFrontend, CancellationToken ct)
    {
        if (RunDockerCompose(root, "docker-compose.deps.yml", "up", "-d") != 0)
            return 1;

        Console.WriteLine("Waiting for Redis and PostgreSQL...");
        if (!await WaitForPortAsync("127.0.0.1", 6379, TimeSpan.FromSeconds(60), ct) ||
            !await WaitForPortAsync("127.0.0.1", 5432, TimeSpan.FromSeconds(60), ct))
        {
            Console.Error.WriteLine("Dependencies did not become ready in time.");
            return 1;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\nShutting down Snekl dev stack...");
            cts.Cancel();
        };

        using var runner = new ProcessRunner();

        var sharedEnv = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ConnectionStrings__Redis"] = "localhost:6379"
        };

        var authEnv = new Dictionary<string, string>(sharedEnv)
        {
            ["ConnectionStrings__Default"] = "Host=localhost;Port=5432;Database=snekl_auth;Username=snekl;Password=snekl",
            ["Auth__BaseUrl"] = AuthUrl,
            ["DOTNET_URLS"] = AuthUrl
        };

        var contentEnv = new Dictionary<string, string>(sharedEnv)
        {
            ["ConnectionStrings__Default"] = "Host=localhost;Port=5432;Database=snekl;Username=snekl;Password=snekl",
            ["Node__BaseUrl"] = ContentUrl,
            ["DOTNET_URLS"] = ContentUrl
        };

        Console.WriteLine($"Starting auth server at {AuthUrl}");
        runner.Start("dotnet", "run --project src/Snekl.Account.Api --no-launch-profile", root, authEnv);

        Console.WriteLine($"Starting content server at {ContentUrl}");
        runner.Start("dotnet", "run --project src/Snekl.Api --no-launch-profile", root, contentEnv);

        if (withFrontend && Directory.Exists(Path.Combine(root, "client/snekl-web")))
        {
            EnsureFrontendEnv(root);
            Console.WriteLine($"Starting frontend at {FrontendUrl}");
            runner.Start("npm", "run dev -- --host 0.0.0.0 --port 5173", Path.Combine(root, "client/snekl-web"));
        }

        Console.WriteLine();
        Console.WriteLine("Snekl dev stack is running:");
        Console.WriteLine($"  Auth:     {AuthUrl}");
        Console.WriteLine($"  Content:  {ContentUrl}");
        if (withFrontend) Console.WriteLine($"  Frontend: {FrontendUrl}");
        Console.WriteLine("Press Ctrl+C to stop.");

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (TaskCanceledException)
        {
            // Expected on Ctrl+C.
        }

        return 0;
    }

    private static void EnsureFrontendEnv(string root)
    {
        var envPath = Path.Combine(root, "client/snekl-web/.env");
        if (File.Exists(envPath)) return;

        var examplePath = Path.Combine(root, "client/snekl-web/.env.example");
        if (File.Exists(examplePath))
            File.Copy(examplePath, envPath);
        else
            File.WriteAllText(envPath, "VITE_API_URL=http://localhost:5001\nVITE_AUTH_URL=http://localhost:5100\n");
    }

    private static async Task<bool> WaitForPortAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, port, ct);
                return true;
            }
            catch
            {
                await Task.Delay(500, ct);
            }
        }

        return false;
    }
}
