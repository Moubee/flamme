using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Elsa.NativeAot.IntegrationTests;

internal static class BuildTest
{
    public static readonly string Root = FindRepository();
    public static string Framework { get; } = $"net{new FrameworkName(AppContext.TargetFrameworkName!).Version.ToString(2)}";
    public static string Rid => RuntimeInformation.RuntimeIdentifier;

    public static string CreateDirectory(string name) =>
        Directory.CreateDirectory(Path.Combine(Root, "artifacts", "nativeaot", Framework, $"{name}-{Guid.NewGuid():N}")).FullName;

    public static async Task<CommandResult> Run(string directory, string log, string executable, params string[] arguments)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        start.Environment["DOTNET_NOLOGO"] = "true";
        // Keep nested builds owned by this process when another test host exits or is cancelled.
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "false";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        // Redirected SDK output is inspected for compiler diagnostics.
        start.Environment["MSBUILDTERMINALLOGGER"] = "off";

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        finally
        {
            var command = string.Join(' ', arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            await File.WriteAllTextAsync(log, $"{directory}> {executable} {command}\nExit code {process.ExitCode}\n{await stdout}\n{await stderr}");
        }
        return new CommandResult(process.ExitCode, (await stdout) + (await stderr), log);
    }

    private static string FindRepository()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Elsa.slnx")))
                    return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("Run the NativeAOT tests from the Flamme checkout.");
    }
}

internal sealed record CommandResult(int ExitCode, string Output, string Log)
{
    public void EnsureSuccess()
    {
        if (ExitCode != 0)
            throw new InvalidOperationException($"Process exited with {ExitCode}. See {Log}\n{Output[^Math.Min(Output.Length, 3000)..]}");
    }
}
