using MedicalNewsVerifier.Web;
using MedicalNewsVerifier.Web.Services;
using MedicalNewsVerifier.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace MedicalNewsVerifier.Web.Controllers;

[ApiController]
[Route("api/analysis")]
[IgnoreAntiforgeryToken]
public sealed class AnalysisApiController(
    IServiceScopeFactory scopeFactory,
    IAnalysisJobStore jobStore,
    IAnalysisJobCancellationRegistry jobCancellationRegistry,
    IAnalysisDefaultsService analysisDefaultsService,
    ILogger<AnalysisApiController> logger) : ControllerBase
{
    [HttpGet("defaults")]
    public ActionResult<AnalysisDefaultsResponse> Defaults()
    {
        var effective = analysisDefaultsService.GetDefaults();
        return Ok(AnalysisDefaultsResponse.FromEffective(effective));
    }

    [HttpPost("start")]
    public IActionResult Start([FromBody] AnalyzeNewsInputModel? input)
    {
        if (input is null)
        {
            return BadRequest(new { message = "Пустое тело запроса или неверный JSON. Убедитесь, что Content-Type: application/json." });
        }

        if (string.IsNullOrWhiteSpace(input.Headline) && !string.IsNullOrWhiteSpace(input.NewsText))
        {
            var firstLine = input.NewsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? string.Empty;
            if (firstLine.Length > 0)
            {
                input.Headline = firstLine.Length <= 500 ? firstLine : firstLine[..500];
            }
        }

        if (string.IsNullOrWhiteSpace(input.SourceUrl))
        {
            input.SourceUrl = null;
        }

        var settingsErrors = AnalysisRunSettingsValidator.Validate(input.RunSettings);
        if (settingsErrors.Count > 0)
        {
            return BadRequest(new { message = string.Join(" ", settingsErrors) });
        }

        input.RunSettings = AnalysisRunSettingsValidator.Clamp(input.RunSettings);

        if (!TryValidateModel(input))
        {
            return ValidationProblem(ModelState);
        }

        var jobId = Guid.NewGuid();
        jobStore.Create(jobId);
        var cancellationToken = jobCancellationRegistry.Register(jobId);

        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<INewsAnalysisService>();
                await svc.RunAnalysisJobAsync(jobId, input, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                jobStore.Patch(jobId, s =>
                {
                    s.Phase = "Cancelled";
                    s.Error = null;
                    s.Message = "Анализ прерван пользователем.";
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background analysis job {JobId} crashed", jobId);
                jobStore.Patch(jobId, s =>
                {
                    s.Phase = "Failed";
                    s.Error = ex.Message;
                });
            }
            finally
            {
                jobCancellationRegistry.Remove(jobId);
            }
        });

        return Ok(new { jobId });
    }

    [HttpPost("{jobId:guid}/cancel")]
    public IActionResult Cancel(Guid jobId)
    {
        var state = jobStore.TryGet(jobId);
        if (state is null)
        {
            return NotFound(new { message = "Задание не найдено или уже завершено." });
        }

        if (state.Phase is "Completed" or "Failed" or "Cancelled")
        {
            return BadRequest(new { message = "Анализ уже завершён." });
        }

        if (!jobCancellationRegistry.TryCancel(jobId))
        {
            return NotFound(new { message = "Не удалось прервать задание." });
        }

        jobStore.Patch(jobId, s =>
        {
            s.Phase = "Cancelled";
            s.Error = null;
            s.Message = "Прерывание анализа…";
        });

        return Ok(new { message = "Запрос на прерывание отправлен." });
    }

    [HttpGet("{jobId:guid}/status")]
    public ActionResult<AnalysisJobState> Status(Guid jobId)
    {
        var state = jobStore.TryGet(jobId);
        return state is null ? NotFound() : Ok(state);
    }
}

public sealed class AnalysisDefaultsResponse
{
    public bool OllamaEnabled { get; init; }
    public bool OllamaGloballyEnabled { get; init; }
    public int MaxCorpusSnippets { get; init; }
    public int MaxCorpusCharsPerSnippet { get; init; }
    public int MaxResponseTokens { get; init; }
    public double Temperature { get; init; }
    public double TopP { get; init; }
    public bool EnableThinking { get; init; }
    public int MaxArticlesPerAnalysis { get; init; }
    public int MinRelevanceScore { get; init; }
    public int MinzdravMaxFeedScan { get; init; }
    public double HeuristicBlendWeight { get; init; }
    public double LlmBlendWeight { get; init; }
    public int PythonTimeoutSeconds { get; init; }
    public bool PythonEnableNatasha { get; init; }
    public bool PythonEnableStanza { get; init; }

    public static AnalysisDefaultsResponse FromEffective(EffectiveAnalysisRunSettings s) => new()
    {
        OllamaEnabled = s.OllamaEnabled,
        OllamaGloballyEnabled = s.OllamaGloballyEnabled,
        MaxCorpusSnippets = s.MaxCorpusSnippets,
        MaxCorpusCharsPerSnippet = s.MaxCorpusCharsPerSnippet,
        MaxResponseTokens = s.MaxResponseTokens,
        Temperature = s.Temperature,
        TopP = s.TopP,
        EnableThinking = s.EnableThinking,
        MaxArticlesPerAnalysis = s.MaxArticlesPerAnalysis,
        MinRelevanceScore = s.MinRelevanceScore,
        MinzdravMaxFeedScan = s.MinzdravMaxFeedScan,
        HeuristicBlendWeight = s.HeuristicBlendWeight,
        LlmBlendWeight = s.LlmBlendWeight,
        PythonTimeoutSeconds = s.PythonTimeoutSeconds,
        PythonEnableNatasha = s.PythonEnableNatasha,
        PythonEnableStanza = s.PythonEnableStanza
    };
}
