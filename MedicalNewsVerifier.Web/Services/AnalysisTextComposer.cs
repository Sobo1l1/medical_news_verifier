namespace MedicalNewsVerifier.Web.Services;

/// <summary>
/// Заголовок и текст новости в виде, совпадающем с анализом: FullText = Headline + ". " + NewsBody.
/// </summary>
public readonly record struct AnalyzedDocument(string Headline, string NewsBody)
{
    public const string Separator = ". ";

    public string FullText => $"{Headline}{Separator}{NewsBody}";

    /// <summary>Индекс начала текста новости в <see cref="FullText"/>.</summary>
    public int BodyStart => Headline.Length + Separator.Length;

    public static AnalyzedDocument From(string headline, string newsText) =>
        new(headline.Trim(), newsText.Trim());

    /// <summary>Совместимость со старым вызовом.</summary>
    public static string Compose(string headline, string newsText) => From(headline, newsText).FullText;
}
