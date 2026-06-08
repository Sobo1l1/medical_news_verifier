using System.Diagnostics;
using MedicalNewsVerifier.Web.Data;
using MedicalNewsVerifier.Web.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedicalNewsVerifier.Web.Services;

public interface ISystemDiagnosticsService
{
    Task<DiagnosticsViewModel> CheckAsync(CancellationToken cancellationToken);
}

public sealed class SystemDiagnosticsService(
    AppDbContext db,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    IOptions<OllamaOptions> ollamaOptions,
    ILogger<SystemDiagnosticsService> logger) : ISystemDiagnosticsService
{
    public async Task<DiagnosticsViewModel> CheckAsync(CancellationToken cancellationToken)
    {
        var vm = new DiagnosticsViewModel
        {
            OllamaEnabled = configuration.GetValue("Ollama:Enabled", false),
            OllamaModel = ollamaOptions.Value.Model
        };

        try
        {
            await db.Database.CanConnectAsync(cancellationToken);
            vm.DatabaseOk = true;
        }
        catch (Exception ex)
        {
            vm.DatabaseOk = false;
            vm.DatabaseError = ex.Message;
            logger.LogWarning(ex, "Database diagnostics failed");
        }

        if (vm.OllamaEnabled)
        {
            try
            {
                var baseUrl = ollamaOptions.Value.BaseUrl.TrimEnd('/');
                if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                {
                    baseUrl = baseUrl[..^3];
                }

                var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                using var response = await client.GetAsync($"{baseUrl}/api/tags", cancellationToken);
                vm.OllamaReachable = response.IsSuccessStatusCode;
                if (!response.IsSuccessStatusCode)
                {
                    vm.OllamaError = $"HTTP {(int)response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                vm.OllamaReachable = false;
                vm.OllamaError = ex.Message;
            }
        }

        try
        {
            var pythonPath = configuration["Python:ExecutablePath"] ?? "python";
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            vm.PythonAvailable = process.ExitCode == 0;
            vm.PythonVersion = string.IsNullOrWhiteSpace(output) ? null : output.Trim();
            if (!vm.PythonAvailable)
            {
                vm.PythonError = "Python не найден или вернул ошибку";
            }
        }
        catch (Exception ex)
        {
            vm.PythonAvailable = false;
            vm.PythonError = ex.Message;
        }

        return vm;
    }
}
