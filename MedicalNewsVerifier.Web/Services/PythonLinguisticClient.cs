using System.Diagnostics;
using System.Text.Json;

namespace MedicalNewsVerifier.Web.Services;

public class PythonLinguisticClient(IConfiguration configuration, IWebHostEnvironment env, ILogger<PythonLinguisticClient> logger) : IPythonLinguisticClient
{
    private const int StderrPreviewMaxChars = 400;

    public async Task<PythonAnalysisOutcome> AnalyzeAsync(string text, CancellationToken cancellationToken)
    {
        var pythonExe = configuration["Python:ExecutablePath"] ?? "python";
        var scriptRelativePath = configuration["Python:ScriptPath"] ?? "python/analyze_text.py";
        var scriptPath = Path.Combine(env.ContentRootPath, scriptRelativePath);
        var timeoutSeconds = configuration.GetValue<int?>("Python:TimeoutSeconds") ?? 8;

        if (!File.Exists(scriptPath))
        {
            logger.LogWarning("Python script not found at {Path}", scriptPath);
            return new PythonAnalysisOutcome([], PythonAnalysisStatus.ScriptMissing);
        }

        var lexiconRelative = configuration["Python:LexiconRoot"] ?? "Resources/Lexicons";
        var lexiconRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, lexiconRelative));

        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"\"{scriptPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["MEDNEWS_LEXICON_ROOT"] = lexiconRoot;

        if (configuration.GetValue<bool>("Python:EnableNatasha"))
        {
            startInfo.Environment["MEDNEWS_ENABLE_NATASHA"] = "1";
        }

        if (configuration.GetValue<bool>("Python:EnableStanza"))
        {
            startInfo.Environment["MEDNEWS_ENABLE_STANZA"] = "1";
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to start python executable: {PythonExe}", pythonExe);
            return new PythonAnalysisOutcome([], PythonAnalysisStatus.StartFailed);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await process.StandardInput.WriteAsync(text);
            process.StandardInput.Close();

            var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var error = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            if (process.ExitCode != 0)
            {
                logger.LogWarning("Python analysis failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
                return new PythonAnalysisOutcome(
                    [],
                    PythonAnalysisStatus.NonZeroExit,
                    process.ExitCode,
                    TruncateStderr(error));
            }

            var trimmed = output.Trim();
            if (trimmed.Length == 0)
            {
                logger.LogWarning("Python analysis returned empty stdout");
                return new PythonAnalysisOutcome([], PythonAnalysisStatus.JsonError, process.ExitCode, TruncateStderr(error));
            }

            try
            {
                var list = JsonSerializer.Deserialize<List<PythonFragmentResult>>(trimmed, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (list is null)
                {
                    logger.LogWarning("Python analysis JSON deserialized to null");
                    return new PythonAnalysisOutcome([], PythonAnalysisStatus.JsonError, process.ExitCode, TruncateStderr(error));
                }

                return new PythonAnalysisOutcome(list, PythonAnalysisStatus.Ok, process.ExitCode, TruncateStderr(error));
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Python analysis JSON parse failed; stdout length {Len}", trimmed.Length);
                return new PythonAnalysisOutcome([], PythonAnalysisStatus.JsonError, process.ExitCode, TruncateStderr(error));
            }
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            logger.LogWarning("Python analysis timeout after {TimeoutSeconds} seconds", timeoutSeconds);
            return new PythonAnalysisOutcome([], PythonAnalysisStatus.Timeout);
        }
    }

    private static string? TruncateStderr(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        var t = error.Trim();
        return t.Length <= StderrPreviewMaxChars ? t : t[..StderrPreviewMaxChars] + "…";
    }
}
