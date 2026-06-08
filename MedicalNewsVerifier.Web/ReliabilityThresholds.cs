namespace MedicalNewsVerifier.Web;

public static class ReliabilityThresholds
{
    public const int SuspiciousMax = 40;
    public const int ReliableMin = 75;

    public static string StatusBadgeClass(int score) => score switch
    {
        >= ReliableMin => "success",
        <= SuspiciousMax => "danger",
        _ => "warning"
    };

    public static string StatusLabel(int score) => score switch
    {
        >= ReliableMin => "Вероятно достоверно",
        <= SuspiciousMax => "Подозрительно",
        _ => "Требует проверки"
    };
}
