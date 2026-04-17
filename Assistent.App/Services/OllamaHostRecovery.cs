using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Assistent.App.Services;

/// <summary>
/// When a chat request fails, optionally stops a stuck Ollama host and runs <c>ollama serve</c> again, then the caller can retry.
/// </summary>
public sealed class OllamaHostRecovery
{
    private readonly ILogger<OllamaHostRecovery> _logger;

    public OllamaHostRecovery(ILogger<OllamaHostRecovery> logger) => _logger = logger;

    /// <summary>
    /// If any process named <c>Ollama</c> is running, try to exit it then start <c>ollama serve</c>. Runs work on a thread-pool thread.
    /// </summary>
    /// <returns><c>true</c> if an Ollama process was found and terminated (restart may still fail silently).</returns>
    public async Task<bool> TryKillRunningHostAndRestartServeAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => TryKillRunningHostAndRestartServeCore(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private bool TryKillRunningHostAndRestartServeCore(CancellationToken cancellationToken)
    {
        List<Process> processes;
        try
        {
            var byName = Process.GetProcessesByName("Ollama")
                .Concat(Process.GetProcessesByName("ollama"))
                .GroupBy(p => p.Id)
                .Select(g => g.First())
                .ToList();
            processes = byName;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama recovery: could not enumerate processes");
            return false;
        }

        if (processes.Count == 0)
        {
            _logger.LogInformation("Ollama recovery: no running Ollama process found");
            return false;
        }

        _logger.LogWarning("Ollama recovery: stopping {Count} Ollama process(es)", processes.Count);

        foreach (var p in processes)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!p.HasExited)
                {
                    try
                    {
                        if (p.CloseMainWindow())
                            p.WaitForExit(milliseconds: 3000);
                    }
                    catch
                    {
                        /* ignore */
                    }

                    if (!p.HasExited)
                        p.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ollama recovery: failed to stop PID {Pid}", p.Id);
            }
            finally
            {
                try
                {
                    p.Dispose();
                }
                catch
                {
                    /* ignore */
                }
            }
        }

        try
        {
            Thread.Sleep(1500);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = "serve",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });
            _logger.LogInformation("Ollama recovery: started ollama serve");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama recovery: could not start ollama serve (ensure Ollama is on PATH)");
        }

        return true;
    }
}
