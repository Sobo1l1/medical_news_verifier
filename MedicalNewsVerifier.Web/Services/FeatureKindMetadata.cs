using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Services;

public static class FeatureKindMetadata
{
    public static string Title(SuspiciousFeatureKind kind) => kind switch
    {
        SuspiciousFeatureKind.Emotional => "Эмоционально окрашенная лексика",
        SuspiciousFeatureKind.Manipulative => "Манипулятивные выражения",
        SuspiciousFeatureKind.Evaluative => "Оценочная лексика",
        SuspiciousFeatureKind.UppercaseWord => "Слова в верхнем регистре",
        SuspiciousFeatureKind.Exclamation => "Восклицательные знаки",
        SuspiciousFeatureKind.Question => "Вопросительные знаки",
        SuspiciousFeatureKind.Link => "Ссылки",
        SuspiciousFeatureKind.Date => "Даты",
        SuspiciousFeatureKind.Number => "Числовые данные",
        SuspiciousFeatureKind.SourceCue => "Указание на источник",
        SuspiciousFeatureKind.PythonHeuristic => "Формулировки повышенного риска (доп. модуль)",
        _ => "Прочее"
    };

    public static string Description(SuspiciousFeatureKind kind) => kind switch
    {
        SuspiciousFeatureKind.Emotional =>
            "Слова из словаря эмоциональной лексики повышают риск субъективной подачи.",
        SuspiciousFeatureKind.Manipulative =>
            "Устойчивые формулировки, часто используемые для давления на читателя.",
        SuspiciousFeatureKind.Evaluative =>
            "Оценочные слова без измеримых критериев снижают нейтральность текста.",
        SuspiciousFeatureKind.UppercaseWord =>
            "Избыточный верхний регистр может использоваться для акцента и эмоционального давления.",
        SuspiciousFeatureKind.Exclamation =>
            "Частые или повторяющиеся восклицательные знаки усиливают эмоциональный тон.",
        SuspiciousFeatureKind.Question =>
            "Множественные вопросительные знаки могут указывать на риторический приём вместо фактов.",
        SuspiciousFeatureKind.Link =>
            "Наличие ссылки облегчает проверку первоисточника.",
        SuspiciousFeatureKind.Date =>
            "Конкретные даты помогают сопоставить сообщение с хронологией событий.",
        SuspiciousFeatureKind.Number =>
            "Числовые данные при наличии контекста обычно повышают проверяемость утверждений.",
        SuspiciousFeatureKind.SourceCue =>
            "Явные отсылки к источнику повышают прозрачность происхождения информации.",
        SuspiciousFeatureKind.PythonHeuristic =>
            "Отдельный модуль ищет типичные «красные флаги» в формулировках (абсолютные обещания, опасные советы и т.п.).",
        _ => string.Empty
    };

    /// <summary>CSS-класс без префикса marker-kind-</summary>
    public static string CssToken(SuspiciousFeatureKind kind) => kind switch
    {
        SuspiciousFeatureKind.Emotional => "emotional",
        SuspiciousFeatureKind.Manipulative => "manipulative",
        SuspiciousFeatureKind.Evaluative => "evaluative",
        SuspiciousFeatureKind.UppercaseWord => "uppercase",
        SuspiciousFeatureKind.Exclamation => "exclamation",
        SuspiciousFeatureKind.Question => "question",
        SuspiciousFeatureKind.Link => "source",
        SuspiciousFeatureKind.Date => "date",
        SuspiciousFeatureKind.Number => "number",
        SuspiciousFeatureKind.SourceCue => "source",
        SuspiciousFeatureKind.PythonHeuristic => "python",
        _ => "none"
    };

    /// <summary>Цветовая легенда (CSS-токен без префикса marker-kind-).</summary>
    public static IReadOnlyList<(string CssToken, string Label)> LegendSwatches() =>
    [
        ("emotional", "Эмоциональная лексика"),
        ("manipulative", "Манипулятивные конструкции"),
        ("evaluative", "Оценочная лексика"),
        ("uppercase", "Верхний регистр"),
        ("exclamation", "Восклицательные знаки"),
        ("question", "Вопросительные знаки"),
        ("source", "Ссылки и отсылки к источнику"),
        ("date", "Даты"),
        ("number", "Числа"),
        ("python", "Доп. признаки риска")
    ];

    public static string PdfHighlightColor(SuspiciousFeatureKind kind) => kind switch
    {
        SuspiciousFeatureKind.Emotional => "#F8D7DA",
        SuspiciousFeatureKind.Manipulative => "#E2D9F3",
        SuspiciousFeatureKind.Evaluative => "#FFE5CC",
        SuspiciousFeatureKind.UppercaseWord => "#F7D6E6",
        SuspiciousFeatureKind.Exclamation => "#FFF3CD",
        SuspiciousFeatureKind.Question => "#CFF4FC",
        SuspiciousFeatureKind.Link => "#D1E7DD",
        SuspiciousFeatureKind.Date => "#D1F2EB",
        SuspiciousFeatureKind.Number => "#E2E3E5",
        SuspiciousFeatureKind.SourceCue => "#D6E4FF",
        SuspiciousFeatureKind.PythonHeuristic => "#E2E3E5",
        _ => "#F8F9FA"
    };
}
