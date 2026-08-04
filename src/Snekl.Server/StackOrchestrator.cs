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

    public Process Start(string fileName, IReadOnlyList<string> arguments, string workingDirectory, IDictionary<string, string>? environment = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

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

    public static int RunToCompletion(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(stdout))
            Console.Write(stdout);
        if (!string.IsNullOrWhiteSpace(stderr))
            Console.Error.Write(stderr);

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
    private const string ContentBetaUrl = "http://localhost:5002";
    private const string AuthUrl = "http://localhost:5100";
    private const string FrontendUrl = "http://localhost:5173";

    public static async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        var command = args.FirstOrDefault(a => !a.StartsWith('-'))?.ToLowerInvariant() ?? "dev";
        var root = RepoPaths.FindRoot(Directory.GetCurrentDirectory());
        var withFrontend = !args.Contains("--no-frontend");
        var resetDb = args.Contains("--reset-db");
        var force = args.Contains("--force");

        return command switch
        {
            "dev" => await RunDevAsync(root, withFrontend, resetDb, ct),
            "status" => DevProcessCommands.RunStatus(root),
            "stop" => DevProcessCommands.RunStop(root, force),
            "up" => RunDockerCompose(root, "docker-compose.yml", ["up", "--build"]),
            "down" => RunDockerCompose(root, "docker-compose.yml", ["down"]),
            "deps" => RunDockerCompose(root, "docker-compose.deps.yml", ["up", "-d"]),
            "deps-down" => RunDockerCompose(root, "docker-compose.deps.yml", ["down"]),
            "help" or "--help" or "-h" => PrintHelp(),
            _ => UnknownCommand(command)
        };
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

            Usage:
              dotnet run --project src/Snekl.Server --no-launch-profile -- [command] [options]

            Commands:
              dev         Start Redis + Postgres (Docker), auth, content alpha/beta, and frontend locally
              status      Show running Snekl APIs and recent request activity
              stop        List running APIs; add --force to stop them
              up          Start the full Docker stack (docker compose up --build)
              down        Stop the full Docker stack
              deps        Start only Redis + Postgres for local development
              deps-down   Stop Redis + Postgres
              help        Show this help

            Options:
              --no-frontend   Skip starting the React dev server during 'dev'
              --reset-db      Wipe auth/content databases before starting (fresh dev state)
              --force         With 'stop', kill running Snekl API processes
            """);
        return 0;
    }

    private static int RunDockerCompose(string root, string composeFile, IReadOnlyList<string> composeArgs)
    {
        var composePath = Path.Combine(root, composeFile);
        if (!File.Exists(composePath))
        {
            Console.Error.WriteLine($"Compose file not found: {composePath}");
            return 1;
        }

        var arguments = new List<string> { "compose", "-f", composePath };
        arguments.AddRange(composeArgs);
        return ProcessRunner.RunToCompletion("docker", arguments, root);
    }

    private static async Task<int> RunDevAsync(string root, bool withFrontend, bool resetDb, CancellationToken ct)
    {
        if (resetDb)
            Console.WriteLine("Resetting dev databases (--reset-db)...");
        if (RunDockerCompose(root, "docker-compose.deps.yml", ["up", "-d"]) != 0)
        {
            Console.Error.WriteLine("Failed to start Redis/PostgreSQL. Is Docker running?");
            return 1;
        }

        Console.WriteLine("Waiting for Redis and PostgreSQL...");
        if (!await WaitForPortAsync("127.0.0.1", 6379, TimeSpan.FromSeconds(60), ct) ||
            !await WaitForPortAsync("127.0.0.1", 5432, TimeSpan.FromSeconds(60), ct))
        {
            Console.Error.WriteLine("Dependencies did not become ready in time.");
            return 1;
        }

        Console.WriteLine("Building API projects...");
        if (ProcessRunner.RunToCompletion("dotnet", ["build", "src/Snekl.Account.Api/Snekl.Account.Api.csproj", "-c", "Debug"], root) != 0 ||
            ProcessRunner.RunToCompletion("dotnet", ["build", "src/Snekl.Api/Snekl.Api.csproj", "-c", "Debug"], root) != 0)
        {
            Console.Error.WriteLine("Build failed. A running Snekl API may be locking build output.");
            Console.Error.WriteLine("Check activity: dotnet run --project src/Snekl.Server --no-launch-profile -- status");
            Console.Error.WriteLine("Stop when safe:  dotnet run --project src/Snekl.Server --no-launch-profile -- stop --force");
            return 1;
        }

        if (IsPortInUse(5100) || IsPortInUse(5001) || IsPortInUse(5002))
        {
            Console.Error.WriteLine("Ports 5100, 5001, or 5002 are already in use. Stop the previous Snekl dev session first.");
            return 1;
        }

        EnsurePostgresDatabase(root, "snekl_beta");

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

        if (resetDb)
            sharedEnv["Snekl__ResetDb"] = "true";

        var authEnv = new Dictionary<string, string>(sharedEnv)
        {
            ["ConnectionStrings__Default"] = "Host=localhost;Port=5432;Database=snekl_auth;Username=snekl;Password=snekl",
            ["Auth__BaseUrl"] = AuthUrl,
            ["DOTNET_URLS"] = AuthUrl
        };

        var contentEnv = new Dictionary<string, string>(sharedEnv)
        {
            ["ConnectionStrings__Default"] = "Host=localhost;Port=5432;Database=snekl;Username=snekl;Password=snekl",
            ["Node__Name"] = "Snekl Alpha",
            ["Node__BaseUrl"] = ContentUrl,
            ["DOTNET_URLS"] = ContentUrl
        };

        var contentBetaEnv = new Dictionary<string, string>(sharedEnv)
        {
            ["ConnectionStrings__Default"] = "Host=localhost;Port=5432;Database=snekl_beta;Username=snekl;Password=snekl",
            ["Node__Name"] = "Snekl Beta",
            ["Node__BaseUrl"] = ContentBetaUrl,
            ["Node__Description"] = "Secondary federated forum node",
            ["DOTNET_URLS"] = ContentBetaUrl
        };

        Console.WriteLine($"Starting auth server at {AuthUrl}");
        runner.Start("dotnet", ["run", "--project", "src/Snekl.Account.Api", "--no-launch-profile", "--no-build"], root, authEnv);

        Console.WriteLine($"Starting content server (alpha) at {ContentUrl}");
        runner.Start("dotnet", ["run", "--project", "src/Snekl.Api", "--no-launch-profile", "--no-build"], root, contentEnv);

        Console.WriteLine($"Starting content server (beta) at {ContentBetaUrl}");
        runner.Start("dotnet", ["run", "--project", "src/Snekl.Api", "--no-launch-profile", "--no-build"], root, contentBetaEnv);

        if (withFrontend && Directory.Exists(Path.Combine(root, "client/snekl-web")))
        {
            EnsureFrontendEnv(root);
            if (!TryStartFrontend(runner, root))
                return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Snekl dev stack is running:");
        Console.WriteLine($"  Auth:           {AuthUrl}");
        Console.WriteLine($"  Content alpha:  {ContentUrl}");
        Console.WriteLine($"  Content beta:   {ContentBetaUrl}");
        if (withFrontend) Console.WriteLine($"  Frontend:       {FrontendUrl}");
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

    private static bool TryStartFrontend(ProcessRunner runner, string root)
    {
        var webRoot = Path.Combine(root, "client/snekl-web");
        var viteBin = Path.Combine(webRoot, "node_modules/vite/bin/vite.js");
        if (!File.Exists(viteBin))
        {
            Console.Error.WriteLine("Frontend dependencies are missing. Run: npm install in client/snekl-web");
            return false;
        }

        // Invoke Vite via node directly — nvm's npm.cmd resolves npm relative to cwd and breaks in subfolders.
        Console.WriteLine($"Starting frontend at {FrontendUrl}");
        runner.Start("node", [viteBin, "--host", "0.0.0.0", "--port", "5173"], webRoot);
        return true;
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

    private static void EnsurePostgresDatabase(string root, string databaseName)
    {
        var composePath = Path.Combine(root, "docker-compose.deps.yml");
        ProcessRunner.RunToCompletion("docker", [
            "compose", "-f", composePath, "exec", "-T", "postgres",
            "psql", "-U", "snekl", "-d", "postgres", "-v", "ON_ERROR_STOP=0",
            "-c", $"CREATE DATABASE {databaseName};"
        ], root);
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            using var client = new TcpClient();
            client.Connect("127.0.0.1", port);
            return true;
        }
        catch
        {
            return false;
        }
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
