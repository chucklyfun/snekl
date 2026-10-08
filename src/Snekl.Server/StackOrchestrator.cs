using System.Diagnostics;
using System.Net.Sockets;

namespace Snekl.Server;

public static class CliHints
{
    public static string StatusCommand =>
        OperatingSystem.IsWindows() ? @".\snekl status" : "./snekl status";

    public static string StopCommand =>
        OperatingSystem.IsWindows() ? @".\snekl stop" : "./snekl stop";
}

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

    public static int RunToCompletion(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IDictionary<string, string>? environment = null,
        bool errorsOnly = false)
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

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!errorsOnly || process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(stdout))
                Console.Write(stdout);
            if (!string.IsNullOrWhiteSpace(stderr))
                Console.Error.Write(stderr);
        }

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
    private const string HubUrl = "http://localhost:5200";
    private const string FrontendUrl = "http://localhost:5173";
    private const string FakerUrl = "http://localhost:5300";

    public static async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        var command = args.FirstOrDefault(a => !a.StartsWith('-'))?.ToLowerInvariant() ?? "dev";
        var root = RepoPaths.FindRoot(Directory.GetCurrentDirectory());
        var withFrontend = !args.Contains("--no-frontend");
        var resetDb = args.Contains("--reset-db");
        var errorsOnly = args.Contains("--errors-only");

        return command switch
        {
            "dev" => await RunDevAsync(root, withFrontend, resetDb, withSiblings: false, errorsOnly, ct),
            "all" => await RunDevAsync(root, withFrontend, resetDb, withSiblings: true, errorsOnly, ct),
            "status" => DevProcessCommands.RunStatus(root),
            "stop" => DevProcessCommands.RunStop(root),
            "up" => RunDockerCompose(root, "docker-compose.yml", ["up", "--build"]),
            "down" => RunDockerCompose(root, "docker-compose.yml", ["down"]),
            "deps" => RunDockerCompose(root, "docker-compose.deps.yml", ["up", "-d"]),
            "deps-down" => RunDockerCompose(root, "docker-compose.deps.yml", ["down"]),
            "pack" => PackLinux(root, args),
            "android" => AndroidApk.Build(root),
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
              ./snekl [command] [options]
              .\snekl [command] [options]

            Commands:
              dev         Start Redis + Postgres + MinIO + OpenSearch (Docker), auth, hub, content alpha/beta, Faker, and frontend locally
              all         Same as dev, and also start snekl-extensions and snekl-landing when those repos sit next to this one
              status      Show running Snekl APIs and recent request activity
              stop        Stop running Snekl API processes, including commissions when it is running (skips rebuild so a locked snekl.exe is fine)
              up          Start the full Docker stack (docker compose up --build)
              down        Stop the full Docker stack
              deps        Start only Redis + Postgres + MinIO + OpenSearch for local development
              deps-down   Stop Redis + Postgres + MinIO + OpenSearch
              pack        Build Linux Docker images and write artifacts/snekl-linux-deploy.tar.gz
              android     Build the member SPA into a debug APK
              help        Show this help

            Options:
              --no-frontend   Skip starting the React dev server during 'dev' or 'all'
              --errors-only   During 'dev' or 'all', hide info logs so the port list stays visible. Errors still print.
              --reset-db      Wipe auth/hub/content databases before starting (fresh dev state)
              --version TAG   Image tag for 'pack' (default latest)
              --force         Accepted by 'stop' for older snekl.exe builds (current builds always stop)
            """);
        return 0;
    }

    private static int PackLinux(string root, string[] args)
    {
        var version = "latest";
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--version" && i + 1 < args.Length)
                version = args[i + 1];
        }

        var deployDir = Path.Combine(root, "deploy");
        var composeFile = Path.Combine(deployDir, "docker-compose.yml");
        var buildFile = Path.Combine(deployDir, "docker-compose.build.yml");
        if (!File.Exists(composeFile) || !File.Exists(buildFile))
        {
            Console.Error.WriteLine("deploy/docker-compose.yml (and .build.yml) are missing.");
            return 1;
        }

        var packEnv = new Dictionary<string, string>
        {
            ["SNEKL_VERSION"] = version,
            ["AUTH_HOST"] = "auth.example.com",
            ["HUB_HOST"] = "hub.example.com",
            ["CONTENT_HOST"] = "origin.example.com",
            ["POSTGRES_PASSWORD"] = "pack",
            ["AUTH_SIGNING_KEY"] = "pack-signing-key-not-used-for-build",
            ["AUTH_ADMIN_SIGNING_KEY"] = "pack-admin-signing-key-not-used",
            ["HUB_ADMIN_SIGNING_KEY"] = "pack-hub-admin-signing-key-not-used",
            ["CONTENT_JWT_KEY"] = "pack-content-jwt-key-not-used-min-32",
            ["MINIO_ROOT_USER"] = "snekl",
            ["MINIO_ROOT_PASSWORD"] = "pack-minio",
            ["ACME_EMAIL"] = "pack@localhost"
        };

        Console.WriteLine($"Building Linux images (snekl/auth:{version}, snekl/hub:{version}, snekl/content:{version})...");
        var buildArgs = new List<string>
        {
            "compose",
            "-f", composeFile,
            "-f", buildFile,
            "build",
            "auth",
            "hub",
            "content"
        };
        if (ProcessRunner.RunToCompletion("docker", buildArgs, deployDir, packEnv) != 0)
        {
            Console.Error.WriteLine("Docker image build failed. Is Docker running?");
            return 1;
        }

        var artifactsRoot = Path.Combine(root, "artifacts");
        var outDir = Path.Combine(artifactsRoot, "snekl-linux-deploy");
        if (Directory.Exists(outDir))
            Directory.Delete(outDir, recursive: true);
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(Path.Combine(outDir, "postgres"));
        Directory.CreateDirectory(Path.Combine(outDir, "targets"));

        foreach (var name in new[] { "docker-compose.yml", "Caddyfile", "Caddyfile.internal", ".env.example", "README.md", "up.sh", "down.sh", "gen-env.sh" })
            CopyLinuxText(Path.Combine(deployDir, name), Path.Combine(outDir, name));
        CopyLinuxText(
            Path.Combine(deployDir, "postgres", "init-databases.sql"),
            Path.Combine(outDir, "postgres", "init-databases.sql"));
        foreach (var targetEnv in Directory.GetFiles(Path.Combine(deployDir, "targets"), "*.env"))
            CopyLinuxText(targetEnv, Path.Combine(outDir, "targets", Path.GetFileName(targetEnv)));

        var envExample = Path.Combine(outDir, ".env.example");
        var exampleText = File.ReadAllText(envExample).Replace("SNEKL_VERSION=latest", $"SNEKL_VERSION={version}", StringComparison.Ordinal);
        File.WriteAllText(envExample, ToLinuxNewlines(exampleText));

        var imagesTar = Path.Combine(outDir, "images.tar");
        Console.WriteLine("Saving images to images.tar...");
        if (ProcessRunner.RunToCompletion("docker", [
                "save",
                "-o", imagesTar,
                $"snekl/auth:{version}",
                $"snekl/hub:{version}",
                $"snekl/content:{version}"
            ], root) != 0)
        {
            Console.Error.WriteLine("docker save failed.");
            return 1;
        }

        var tarball = Path.Combine(artifactsRoot, "snekl-linux-deploy.tar.gz");
        if (File.Exists(tarball))
            File.Delete(tarball);

        Console.WriteLine("Writing artifacts/snekl-linux-deploy.tar.gz...");
        if (ProcessRunner.RunToCompletion("tar", ["-czf", tarball, "-C", artifactsRoot, "snekl-linux-deploy"], root) != 0)
        {
            Console.Error.WriteLine("tar failed. The unpacked folder is still at artifacts/snekl-linux-deploy/");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"Package: {tarball}");
        Console.WriteLine("On the Linux host: tar -xzf snekl-linux-deploy.tar.gz && cd snekl-linux-deploy && cp .env.example .env && ./gen-env.sh");
        Console.WriteLine("Then edit hostnames in .env and run ./up.sh   (2 GB default: Redis on, OpenSearch off)");
        return 0;
    }

    /// <summary>Pack text as LF so a Windows checkout still produces a Linux-runnable tarball.</summary>
    private static void CopyLinuxText(string source, string destination)
    {
        File.WriteAllText(destination, ToLinuxNewlines(File.ReadAllText(source)));
    }

    private static string ToLinuxNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    private static int RunDockerCompose(string root, string composeFile, IReadOnlyList<string> composeArgs, bool errorsOnly = false)
    {
        var composePath = Path.Combine(root, composeFile);
        if (!File.Exists(composePath))
        {
            Console.Error.WriteLine($"Compose file not found: {composePath}");
            return 1;
        }

        var arguments = new List<string> { "compose", "-f", composePath };
        arguments.AddRange(composeArgs);
        return ProcessRunner.RunToCompletion("docker", arguments, root, errorsOnly: errorsOnly);
    }

    private static async Task<int> RunDevAsync(string root, bool withFrontend, bool resetDb, bool withSiblings, bool errorsOnly, CancellationToken ct)
    {
        if (resetDb)
            Console.WriteLine("Resetting dev databases (--reset-db)...");
        if (RunDockerCompose(root, "docker-compose.deps.yml", ["up", "-d"], errorsOnly) != 0)
        {
            Console.Error.WriteLine("Failed to start Redis/PostgreSQL/MinIO/OpenSearch. Is Docker running?");
            return 1;
        }

        Console.WriteLine("Waiting for Redis, PostgreSQL, MinIO, and OpenSearch...");
        if (!await WaitForPortAsync("127.0.0.1", 6379, TimeSpan.FromSeconds(60), ct) ||
            !await WaitForPortAsync("127.0.0.1", 5432, TimeSpan.FromSeconds(60), ct) ||
            !await WaitForPortAsync("127.0.0.1", 9000, TimeSpan.FromSeconds(60), ct) ||
            !await WaitForPortAsync("127.0.0.1", 9200, TimeSpan.FromSeconds(120), ct))
        {
            Console.Error.WriteLine("Dependencies did not become ready in time.");
            return 1;
        }

        Console.WriteLine("Building API projects...");
        if (ProcessRunner.RunToCompletion("dotnet", ["build", "src/Snekl.Account.Api/Snekl.Account.Api.csproj", "-c", "Debug"], root, errorsOnly: errorsOnly) != 0 ||
            ProcessRunner.RunToCompletion("dotnet", ["build", "src/Snekl.Hub/Snekl.Hub.csproj", "-c", "Debug"], root, errorsOnly: errorsOnly) != 0 ||
            ProcessRunner.RunToCompletion("dotnet", ["build", "src/Snekl.Api/Snekl.Api.csproj", "-c", "Debug"], root, errorsOnly: errorsOnly) != 0 ||
            ProcessRunner.RunToCompletion("dotnet", ["build", "src/Snekl.Faker/Snekl.Faker.csproj", "-c", "Debug"], root, errorsOnly: errorsOnly) != 0)
        {
            Console.Error.WriteLine("Build failed. A running Snekl API may be locking build output.");
            Console.Error.WriteLine($"Check activity: {CliHints.StatusCommand}");
            Console.Error.WriteLine($"Stop when safe:  {CliHints.StopCommand}");
            return 1;
        }

        var extensionsRepo = withSiblings ? SiblingRepo(root, "snekl-extensions", "extensions") : null;
        var landingRepo = withSiblings ? SiblingRepo(root, "snekl-landing", "landing") : null;
        if (IsPortInUse(5100) || IsPortInUse(5200) || IsPortInUse(5001) || IsPortInUse(5002) || IsPortInUse(5300)
            || (extensionsRepo is not null && IsPortInUse(5301)))
        {
            var ports = extensionsRepo is not null
                ? "5100, 5200, 5001, 5002, 5300, or 5301"
                : "5100, 5200, 5001, 5002, or 5300";
            Console.Error.WriteLine($"Ports {ports} are already in use. Stop the previous Snekl dev session first.");
            return 1;
        }

        EnsurePostgresDatabase(root, "snekl_beta", errorsOnly);
        EnsurePostgresDatabase(root, "snekl_hub", errorsOnly);

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
            ["ConnectionStrings__Redis"] = "localhost:6379",
            ["ConnectionStrings__OpenSearch"] = "http://localhost:9200"
        };

        if (resetDb)
            sharedEnv["Snekl__ResetDb"] = "true";
        if (errorsOnly)
            ApplyErrorsOnlyLogging(sharedEnv);

        var authEnv = new Dictionary<string, string>(sharedEnv)
        {
            ["ConnectionStrings__Default"] = "Host=localhost;Port=5432;Database=snekl_auth;Username=snekl;Password=snekl",
            ["Auth__BaseUrl"] = AuthUrl,
            // Bind all interfaces so LAN clients (e.g. http://192.168.x.x:5173) can reach the API.
            ["DOTNET_URLS"] = "http://0.0.0.0:5100"
        };

        var hubEnv = new Dictionary<string, string>(sharedEnv)
        {
            ["ConnectionStrings__Hub"] = "Host=localhost;Port=5432;Database=snekl_hub;Username=snekl;Password=snekl",
            ["Hub__Name"] = "Snekl Hub",
            ["Hub__BaseUrl"] = HubUrl,
            ["Hub__AuthBaseUrl"] = AuthUrl,
            ["DOTNET_URLS"] = "http://0.0.0.0:5200"
        };

        var contentEnv = new Dictionary<string, string>(sharedEnv)
        {
            ["ConnectionStrings__Default"] = "Host=localhost;Port=5432;Database=snekl;Username=snekl;Password=snekl",
            ["Node__Name"] = "Snekl Alpha",
            ["Node__BaseUrl"] = ContentUrl,
            ["OpenSearch__Index"] = "snekl-posts-alpha",
            ["DOTNET_URLS"] = "http://0.0.0.0:5001"
        };

        var contentBetaEnv = new Dictionary<string, string>(sharedEnv)
        {
            ["ConnectionStrings__Default"] = "Host=localhost;Port=5432;Database=snekl_beta;Username=snekl;Password=snekl",
            ["Node__Name"] = "Snekl Beta",
            ["Node__BaseUrl"] = ContentBetaUrl,
            ["Node__Description"] = "Secondary federated nest node",
            ["OpenSearch__Index"] = "snekl-posts-beta",
            ["DOTNET_URLS"] = "http://0.0.0.0:5002"
        };

        Console.WriteLine($"Starting auth server at {AuthUrl}");
        runner.Start("dotnet", ["run", "--project", "src/Snekl.Account.Api", "--no-launch-profile", "--no-build"], root, authEnv);

        Console.WriteLine($"Starting hub at {HubUrl}");
        runner.Start("dotnet", ["run", "--project", "src/Snekl.Hub", "--no-launch-profile", "--no-build"], root, hubEnv);

        Console.WriteLine($"Starting content server (alpha) at {ContentUrl}");
        runner.Start("dotnet", ["run", "--project", "src/Snekl.Api", "--no-launch-profile", "--no-build"], root, contentEnv);

        Console.WriteLine($"Starting content server (beta) at {ContentBetaUrl}");
        runner.Start("dotnet", ["run", "--project", "src/Snekl.Api", "--no-launch-profile", "--no-build"], root, contentBetaEnv);

        var fakerEnv = new Dictionary<string, string>(sharedEnv)
        {
            ["DOTNET_URLS"] = "http://0.0.0.0:5300",
            ["Faker__ContentBaseUrl"] = ContentUrl,
            ["Faker__ContentBaseUrls__0"] = ContentUrl,
            ["Faker__ContentBaseUrls__1"] = ContentBetaUrl
        };
        Console.WriteLine($"Starting Faker extension at {FakerUrl}");
        runner.Start("dotnet", ["run", "--project", "src/Snekl.Faker", "--no-launch-profile", "--no-build"], root, fakerEnv);

        if (withFrontend && Directory.Exists(Path.Combine(root, "client/snekl-web")))
        {
            EnsureFrontendEnv(root);
            if (!TryStartFrontend(runner, root, errorsOnly))
                return 1;
        }

        if (withSiblings)
            StartSiblings(runner, extensionsRepo, landingRepo, errorsOnly);

        Console.WriteLine();
        Console.WriteLine("Snekl dev stack is running:");
        Console.WriteLine($"  Auth:           {AuthUrl} (listening on 0.0.0.0:5100)");
        Console.WriteLine($"  Hub:            {HubUrl} (listening on 0.0.0.0:5200)");
        Console.WriteLine($"  Content alpha:  {ContentUrl} (listening on 0.0.0.0:5001)");
        Console.WriteLine($"  Content beta:   {ContentBetaUrl} (listening on 0.0.0.0:5002)");
        Console.WriteLine($"  Faker:          {FakerUrl} (listening on 0.0.0.0:5300)");
        if (extensionsRepo is not null)
            Console.WriteLine("  Commissions:    http://localhost:5301");
        if (landingRepo is not null)
            Console.WriteLine("  Landing:        http://localhost:5400");
        if (withFrontend)
        {
            Console.WriteLine($"  Frontend:       {FrontendUrl}");
            Console.WriteLine("  Tip: open the SPA via your LAN IP (e.g. http://192.168.x.x:5173);");
            Console.WriteLine("       the client rewrites localhost service URLs to that host.");
        }
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

    private static string? SiblingRepo(string sneklRoot, string folderName, string scriptStem)
    {
        var dir = Path.GetFullPath(Path.Combine(sneklRoot, "..", folderName));
        if (!Directory.Exists(dir))
            return null;

        var script = Path.Combine(dir, OperatingSystem.IsWindows() ? scriptStem + ".cmd" : scriptStem);
        return File.Exists(script) ? dir : null;
    }

    private static void ApplyErrorsOnlyLogging(IDictionary<string, string> env)
    {
        env["Logging__LogLevel__Default"] = "Error";
        env["Logging__LogLevel__Microsoft.AspNetCore"] = "Error";
        env["Logging__LogLevel__Microsoft.EntityFrameworkCore"] = "Error";
        env["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Error";
    }

    private static void StartSiblings(ProcessRunner runner, string? extensionsRepo, string? landingRepo, bool errorsOnly)
    {
        if (extensionsRepo is null)
            Console.WriteLine("Skipping snekl-extensions: repo or extensions script was not found next to this repo.");
        else
            StartExtensions(runner, extensionsRepo, errorsOnly);

        if (landingRepo is null)
            Console.WriteLine("Skipping snekl-landing: repo or landing script was not found next to this repo.");
        else
            StartLanding(runner, landingRepo);
    }

    private static void StartExtensions(ProcessRunner runner, string repoDir, bool errorsOnly)
    {
        var project = Path.Combine(repoDir, "commissions", "Snekl.Commissions", "Snekl.Commissions.csproj");
        if (!File.Exists(project))
        {
            Console.WriteLine("Skipping commissions: project was not found.");
            return;
        }

        if (!HasProject(Path.Combine(repoDir, "calendar")))
            Console.WriteLine("Calendar has no server yet; skipping.");

        Console.WriteLine("Starting commissions at http://localhost:5301");
        var commissionsEnv = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["DOTNET_URLS"] = "http://0.0.0.0:5301"
        };
        if (errorsOnly)
            ApplyErrorsOnlyLogging(commissionsEnv);
        var commissionsArgs = new List<string> { "run", "--project", project, "--no-launch-profile" };
        if (errorsOnly)
            commissionsArgs.AddRange(["-v", "q"]);
        runner.Start("dotnet", commissionsArgs, repoDir, commissionsEnv);
    }

    private static void StartLanding(ProcessRunner runner, string repoDir)
    {
        if (!File.Exists(Path.Combine(repoDir, "package.json")))
        {
            Console.WriteLine("The landing site is not started yet.");
            return;
        }

        var viteBin = Path.Combine(repoDir, "node_modules", "vite", "bin", "vite.js");
        if (!File.Exists(viteBin))
        {
            Console.Error.WriteLine("Landing dependencies are missing. Run: npm install in the snekl-landing repo");
            return;
        }

        // Invoke Vite via node directly — nvm's npm.cmd resolves npm relative to cwd and breaks in subfolders.
        Console.WriteLine("Starting the landing site at http://localhost:5400");
        runner.Start("node", [viteBin], repoDir);
    }

    private static bool HasProject(string directory) =>
        Directory.Exists(directory) &&
        Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories).Any();

    private static bool TryStartFrontend(ProcessRunner runner, string root, bool errorsOnly)
    {
        var webRoot = Path.Combine(root, "client/snekl-web");
        var viteBin = Path.Combine(webRoot, "node_modules/vite/bin/vite.js");
        if (!File.Exists(viteBin))
        {
            Console.Error.WriteLine("Frontend dependencies are missing. Run: npm install in client/snekl-web");
            return false;
        }

        var quiet = errorsOnly ? new[] { "--logLevel", "error" } : Array.Empty<string>();
        // Invoke Vite via node directly — nvm's npm.cmd resolves npm relative to cwd and breaks in subfolders.
        Console.WriteLine($"Starting frontend at {FrontendUrl}");
        runner.Start("node", [viteBin, "--host", "0.0.0.0", "--port", "5173", .. quiet], webRoot);
        Console.WriteLine("Watching operator UI into Auth/Hub/Content wwwroot/server");
        runner.Start("node", [viteBin, "build", "--config", "vite.admin.config.ts", "--watch", .. quiet], webRoot);
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
            File.WriteAllText(envPath, "VITE_AUTH_URL=http://localhost:5100\nVITE_ALLOW_DEV_PASSWORD_RESET=true\n");
    }

    private static void EnsurePostgresDatabase(string root, string databaseName, bool errorsOnly)
    {
        var composePath = Path.Combine(root, "docker-compose.deps.yml");
        ProcessRunner.RunToCompletion("docker", [
            "compose", "-f", composePath, "exec", "-T", "postgres",
            "psql", "-U", "snekl", "-d", "postgres", "-v", "ON_ERROR_STOP=0",
            "-c", $"CREATE DATABASE {databaseName};"
        ], root, errorsOnly: errorsOnly);
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
