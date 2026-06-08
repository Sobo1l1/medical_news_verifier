using System.Text.Json.Serialization;
using MedicalNewsVerifier.Web;

namespace MedicalNewsVerifier.Web.Services;
public interface IPythonLinguisticClient
{
    Task<PythonAnalysisOutcome> AnalyzeAsync(
        string text,
        EffectiveAnalysisRunSettings? runSettings,
        CancellationToken cancellationToken);
}

/// <summary>
/// Результат запуска внешнего скрипта Python (отдельно от лексического анализа в приложении на C#).
/// </summary>
public sealed record PythonAnalysisOutcome(
    IReadOnlyList<PythonFragmentResult> Fragments,
    PythonAnalysisStatus Status,
    int? ExitCode = null,
    string? StderrPreview = null);

public enum PythonAnalysisStatus
{
    /// <summary>Процесс завершился успешно, ответ разобран (список может быть пустым).</summary>
    Ok,

    /// <summary>Файл скрипта отсутствует по настроенному пути.</summary>
    ScriptMissing,

    /// <summary>Не удалось запустить исполняемый файл Python.</summary>
    StartFailed,

    /// <summary>Процесс завершился с ненулевым кодом.</summary>
    NonZeroExit,

    /// <summary>Превышен таймаут ожидания.</summary>
    Timeout,

    /// <summary>Вывод stdout не удалось разобрать как JSON-массив фрагментов.</summary>
    JsonError
}

public class PythonFragmentResult
{
    [JsonPropertyName("fragment")]
    public string Fragment { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public int Severity { get; set; }

    /// <summary>Начало в переданной в Python строке (включительно). Отсутствие в JSON = -1.</summary>
    [JsonPropertyName("start")]
    public int? Start { get; set; }

    /// <summary>Конец (исключительно), как в Python slice.</summary>
    [JsonPropertyName("end")]
    public int? End { get; set; }

    /// <summary>emotional | evaluative | manipulative | python — для сопоставления с типом разметки.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }
}
