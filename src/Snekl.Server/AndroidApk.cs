namespace Snekl.Server;

public static class AndroidApk
{
    public static int Build(string root)
    {
        var webDir = Path.Combine(root, "client", "snekl-web");
        var androidDir = Path.Combine(webDir, "android");
        var gradle = Path.Combine(androidDir, OperatingSystem.IsWindows() ? "gradlew.bat" : "gradlew");
        if (!File.Exists(gradle))
        {
            Console.Error.WriteLine("client/snekl-web/android is missing. The Capacitor project has not been added.");
            return 1;
        }

        var sdk = ResolveAndroidSdk();
        if (sdk is null)
        {
            Console.Error.WriteLine("Android SDK not found. Set ANDROID_HOME to the SDK directory.");
            return 1;
        }

        var platform = Path.Combine(sdk, "platforms", "android-36");
        if (!Directory.Exists(platform))
        {
            Console.Error.WriteLine($"Android SDK platform 36 is not installed under {sdk}.");
            Console.Error.WriteLine("Install it with sdkmanager: \"platforms;android-36\" \"build-tools;36.0.0\" \"platform-tools\".");
            return 1;
        }

        var javaHome = ResolveJavaHome();
        if (javaHome is null)
        {
            Console.Error.WriteLine("JDK not found. Set JAVA_HOME to a JDK 17 or newer.");
            return 1;
        }

        EnsureLocalProperties(androidDir, sdk);

        Console.WriteLine("Building the member SPA and syncing the Android project...");
        if (RunShell(webDir, "npm", ["run", "build:android"]) != 0)
        {
            Console.Error.WriteLine("npm run build:android failed.");
            return 1;
        }

        var env = new Dictionary<string, string>
        {
            ["ANDROID_HOME"] = sdk,
            ["ANDROID_SDK_ROOT"] = sdk,
            ["JAVA_HOME"] = javaHome,
        };

        Console.WriteLine("Assembling the debug APK...");
        var gradleArgs = OperatingSystem.IsWindows()
            ? new[] { "gradlew.bat", "assembleDebug" }
            : new[] { "./gradlew", "assembleDebug" };
        if (RunShell(androidDir, gradleArgs[0], gradleArgs[1..], env) != 0)
        {
            Console.Error.WriteLine("gradlew assembleDebug failed.");
            return 1;
        }

        var apk = Path.Combine(androidDir, "app", "build", "outputs", "apk", "debug", "app-debug.apk");
        Console.WriteLine($"Android debug APK: {apk}");
        return File.Exists(apk) ? 0 : 1;
    }

    private static void EnsureLocalProperties(string androidDir, string sdk)
    {
        var props = Path.Combine(androidDir, "local.properties");
        if (File.Exists(props)) return;
        var sdkDir = sdk.Replace("\\", "\\\\", StringComparison.Ordinal);
        File.WriteAllText(props, $"sdk.dir={sdkDir}{Environment.NewLine}");
    }

    private static int RunShell(string workingDirectory, string fileName, IReadOnlyList<string> arguments, IDictionary<string, string>? environment = null)
    {
        using var runner = new ProcessRunner();
        var process = OperatingSystem.IsWindows()
            ? runner.Start("cmd.exe", ["/c", fileName, .. arguments], workingDirectory, environment)
            : runner.Start(fileName, arguments, workingDirectory, environment);
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string? ResolveAndroidSdk()
    {
        foreach (var candidate in SdkCandidates())
        {
            if (Directory.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static IEnumerable<string> SdkCandidates()
    {
        var processHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
        if (!string.IsNullOrWhiteSpace(processHome)) yield return processHome;

        var processRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrWhiteSpace(processRoot)) yield return processRoot;

        if (OperatingSystem.IsWindows())
        {
            var userHome = Environment.GetEnvironmentVariable("ANDROID_HOME", EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(userHome)) yield return userHome;

            var userRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT", EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(userRoot)) yield return userRoot;

            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Android",
                "Sdk");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, "Android", "Sdk");
        yield return Path.Combine(home, "Library", "Android", "sdk");
    }

    private static string? ResolveJavaHome()
    {
        foreach (var candidate in JavaCandidates())
        {
            if (HasJava(candidate)) return candidate;
        }

        return null;
    }

    private static IEnumerable<string> JavaCandidates()
    {
        var processHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(processHome)) yield return processHome;

        if (OperatingSystem.IsWindows())
        {
            var userHome = Environment.GetEnvironmentVariable("JAVA_HOME", EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(userHome)) yield return userHome;

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var microsoft = Path.Combine(programFiles, "Microsoft");
            if (Directory.Exists(microsoft))
            {
                foreach (var dir in Directory.GetDirectories(microsoft, "jdk-*").OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
                    yield return dir;
            }

            var androidJdk = Path.Combine(programFiles, "Android", "openjdk");
            if (Directory.Exists(androidJdk))
            {
                foreach (var dir in Directory.GetDirectories(androidJdk, "jdk-*"))
                    yield return dir;
            }
        }
    }

    private static bool HasJava(string home)
    {
        var binary = OperatingSystem.IsWindows() ? "java.exe" : "java";
        return File.Exists(Path.Combine(home, "bin", binary));
    }
}
