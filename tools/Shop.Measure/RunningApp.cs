using System.Diagnostics;

namespace Shop.Measure;

/// <summary>A published app running as its own process, so memory and CPU are the app's own.</summary>
public sealed class RunningApp : IDisposable
{
    private readonly Process _process;

    private RunningApp(Process process, TimeSpan startupTime)
    {
        _process = process;
        StartupTime = startupTime;
    }

    public TimeSpan StartupTime { get; }

    public long WorkingSetMb
    {
        get
        {
            _process.Refresh();
            return _process.WorkingSet64 / (1024 * 1024);
        }
    }

    public TimeSpan CpuTime
    {
        get
        {
            _process.Refresh();
            return _process.TotalProcessorTime;
        }
    }

    /// <summary>Starts the app and waits until <c>/health</c> answers. The time includes the .NET runtime starting.</summary>
    public static async Task<RunningApp> Start(string dll, int port, Dictionary<string, string> settings)
    {
        var info = new ProcessStartInfo("dotnet", $"\"{dll}\"")
        {
            WorkingDirectory = Path.GetDirectoryName(dll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.Environment["Urls"] = $"http://localhost:{port}";
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        info.Environment["Logging__LogLevel__Default"] = "Warning";
        foreach (var (key, value) in settings)
        {
            info.Environment[key] = value;
        }

        var clock = Stopwatch.StartNew();
        var process = Process.Start(info)!;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        while (clock.Elapsed < TimeSpan.FromSeconds(60))
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"{Path.GetFileName(dll)} stopped while starting.");
            }

            try
            {
                using var response = await http.GetAsync($"http://localhost:{port}/health");
                if (response.IsSuccessStatusCode)
                {
                    return new RunningApp(process, clock.Elapsed);
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(10);
        }

        process.Kill(entireProcessTree: true);
        throw new TimeoutException($"{Path.GetFileName(dll)} did not answer /health within 60 seconds.");
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit();
        }

        _process.Dispose();
    }
}
