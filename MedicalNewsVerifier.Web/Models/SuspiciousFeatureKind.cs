namespace MedicalNewsVerifier.Web.Models;

/// <summary>
/// Тип лингвистического или структурного признака для разметки текста и списка результатов.
/// </summary>
public enum SuspiciousFeatureKind
{
    None = 0,
    Emotional = 1,
    Manipulative = 2,
    Evaluative = 3,
    UppercaseWord = 4,
    Exclamation = 5,
    Question = 6,
    Link = 7,
    Date = 8,
    Number = 9,
    SourceCue = 10,
    PythonHeuristic = 11
}
