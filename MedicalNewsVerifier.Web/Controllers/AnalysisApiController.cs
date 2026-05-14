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
    ILogger<AnalysisApiController> logger) : ControllerBase
{
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

        if (!TryValidateModel(input))
        {
            return ValidationProblem(ModelState);
        }

        var jobId = Guid.NewGuid();
        jobStore.Create(jobId);

        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<INewsAnalysisService>();
                await svc.RunAnalysisJobAsync(jobId, input, CancellationToken.None);
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
        });

        return Ok(new { jobId });
    }

    [HttpGet("{jobId:guid}/status")]
    public ActionResult<AnalysisJobState> Status(Guid jobId)
    {
        var state = jobStore.TryGet(jobId);
        return state is null ? NotFound() : Ok(state);
    }
}
