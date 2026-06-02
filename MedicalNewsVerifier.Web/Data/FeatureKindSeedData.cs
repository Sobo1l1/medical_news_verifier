using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Data;

internal static class FeatureKindSeedData
{
    public static readonly SuspiciousFeatureKindDefinition[] Rows =
    [
        new() { Id = 1, Code = "Emotional", Title = "Эмоционально окрашенная лексика", Description = "Текст содержит эмоциональную окраску и призван влиять на чувства.", CssToken = "emotional" },
        new() { Id = 2, Code = "Manipulative", Title = "Манипулятивные выражения", Description = "Выкристаллизованные словоформы, направленные на управление восприятием.", CssToken = "manipulative" },
        new() { Id = 3, Code = "Evaluative", Title = "Оценочная лексика", Description = "Слова с оценкой, создающие субъективное впечатление.", CssToken = "evaluative" },
        new() { Id = 4, Code = "UppercaseWord", Title = "Слова в верхнем регистре", Description = "Слова или фразы, полностью написанные заглавными буквами.", CssToken = "uppercase" },
        new() { Id = 5, Code = "Exclamation", Title = "Восклицательные знаки", Description = "Восклицательные знаки, усиливающие эмоциональность.", CssToken = "exclamation" },
        new() { Id = 6, Code = "Question", Title = "Вопросительные знаки", Description = "Вопросы, побуждающие к сомнению или уточнению.", CssToken = "question" },
        new() { Id = 7, Code = "Link", Title = "Ссылки", Description = "Адреса и гиперссылки в тексте.", CssToken = "source" },
        new() { Id = 8, Code = "Date", Title = "Даты", Description = "Упоминания дат и временных меток.", CssToken = "date" },
        new() { Id = 9, Code = "Number", Title = "Числовые данные", Description = "Числа и количественные обозначения.", CssToken = "number" },
        new() { Id = 10, Code = "SourceCue", Title = "Указание на источник", Description = "Упоминания источников и ссылок на авторитеты.", CssToken = "source" },
        new() { Id = 11, Code = "PythonHeuristic", Title = "Формулировки повышенного риска", Description = "Признаки, обнаруженные дополнительным Python-модулем.", CssToken = "python" }
    ];
}
