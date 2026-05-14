namespace MedicalNewsVerifier.Web.Models;

public static class VerificationStatusRussian
{
    public static string Title(VerificationStatus status) => status switch
    {
        VerificationStatus.LikelyReliable => "Предварительно: высокая достоверность",
        VerificationStatus.Suspicious => "Предварительно: низкая достоверность",
        VerificationStatus.NeedsReview => "Требуется ручная проверка",
        _ => status.ToString()
    };

    public static string Hint(VerificationStatus status) => status switch
    {
        VerificationStatus.LikelyReliable =>
            "Автоматическая оценка относительно высокая; всё равно сверяйте факты с первоисточниками.",
        VerificationStatus.Suspicious =>
            "Обнаружены сильные признаки риска или слабая связь с официальными материалами.",
        VerificationStatus.NeedsReview =>
            "Результат в «серой зоне»: автоматика не даёт однозначного вывода — нужен взгляд редактора.",
        _ => string.Empty
    };
}
