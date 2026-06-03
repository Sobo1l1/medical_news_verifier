using System.Text.RegularExpressions;

namespace MedicalNewsVerifier.Web.Services.Parsers;

public enum NewsTopic
{
    General,
    Oncology,
    Respiratory
}

public static partial class RelevanceScoring
{
    private static readonly char[] TokenSeparators =
        [' ', ',', '.', ';', ':', '!', '?', '\n', '\r', '\t', '(', ')', '"', '«', '»', '—', '-', '/', '\\'];

    private static readonly HashSet<string> StopWords =
    [
        "этот", "этого", "этой", "этом", "котор", "которых", "который", "которые",
        "будет", "были", "была", "было", "быть", "также", "более", "менее", "очень",
        "после", "перед", "между", "через", "только", "может", "могут", "должен",
        "россии", "россия", "россий", "федерации", "министерство", "министерства",
        "данным", "данные", "согласно", "отметил", "отмечает", "сообщил", "году", "года"
    ];

    private static readonly string[] MedicalShortTerms = ["рак", "орви", "грип", "вич"];

    private static readonly string[] OncologyKeywords =
    [
        "онкол", "злокачествен", "новообразован", "онколог", "диспансер", "рак", "опухол",
        "стади", "химиотерап", "лучев", "герцен", "glavonco", "emis", "емисс", "fedstat",
        "статистик", "диагност", "диспансериза", "698693", "674587", "24106"
    ];

    private static readonly string[] RespiratoryKeywords =
    [
        "грипп", "орви", "простуд", "маск", "вакцин", "антисепт", "респиратор",
        "температур", "каш", "насморк", "инфекц", "вирус", "эпидем"
    ];

    private static readonly string[] StatisticsKeywords =
    [
        "статистик", "показател", "случаев", "случаи", "тысяч", "процент", "доля",
        "заболеваем", "выявляем", "диагностирован", "отчет", "отчёт", "емисс", "fedstat"
    ];

    public static NewsTopic DetectDominantTopic(string headline, string newsText)
    {
        var q = $"{headline} {newsText}".ToLowerInvariant();
        var oncoHits = CountKeywordHits(q, OncologyKeywords);
        var respHits = CountKeywordHits(q, RespiratoryKeywords);
        var statsHits = CountKeywordHits(q, StatisticsKeywords);

        if (oncoHits >= 2 && oncoHits >= respHits)
        {
            return NewsTopic.Oncology;
        }

        if (respHits >= 2 && respHits > oncoHits)
        {
            return NewsTopic.Respiratory;
        }

        if (statsHits >= 2 && oncoHits >= 1)
        {
            return NewsTopic.Oncology;
        }

        return NewsTopic.General;
    }

    public static HashSet<string> ExtractSearchTerms(string headline, string newsText, int maxTerms = 24)
    {
        var headlineTokens = Tokenize(headline, weight: 2);
        var bodyTokens = Tokenize(newsText, weight: 1);

        return headlineTokens
            .Concat(bodyTokens)
            .GroupBy(t => t.Word)
            .Select(g => (Word: g.Key, Weight: g.Sum(x => x.Weight)))
            .OrderByDescending(x => x.Weight)
            .ThenByDescending(x => x.Word.Length)
            .Take(maxTerms)
            .Select(x => x.Word)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static int CalculateRelevance(string headline, string newsText, string candidateText) =>
        CalculateMatchScore(headline, newsText, candidateText);

    public static int CalculateRelevance(string combinedQuery, string candidateText)
    {
        var parts = combinedQuery.Split('\n', 2);
        var headline = parts.Length > 0 ? parts[0] : string.Empty;
        var body = parts.Length > 1 ? parts[1] : combinedQuery;
        return CalculateMatchScore(headline, body, candidateText);
    }

    public static bool ContainsAnyKeyword(string text, IEnumerable<string> keywords)
    {
        var lower = text.ToLowerInvariant();
        return keywords.Any(k => lower.Contains(k, StringComparison.Ordinal));
    }

    public static bool MatchesTopicCluster(string headline, string newsText, string candidateText)
    {
        var topic = DetectDominantTopic(headline, newsText);
        var content = candidateText.ToLowerInvariant();
        return topic switch
        {
            NewsTopic.Oncology => CountKeywordHits(content, OncologyKeywords) >= 2
                && !IsPrimarilyRespiratory(content),
            NewsTopic.Respiratory => CountKeywordHits(content, RespiratoryKeywords) >= 2,
            _ => CalculateMatchScore(headline, newsText, candidateText) >= 15
        };
    }

    public static bool IsPrimarilyRespiratory(string text)
    {
        var lower = text.ToLowerInvariant();
        var resp = CountKeywordHits(lower, RespiratoryKeywords);
        var onco = CountKeywordHits(lower, OncologyKeywords);
        return resp >= 2 && resp > onco;
    }

    public static bool QueryExpectsStatistics(string headline, string newsText)
    {
        var q = $"{headline} {newsText}".ToLowerInvariant();
        return CountKeywordHits(q, StatisticsKeywords) >= 2 || LargeNumberRegex().IsMatch(q);
    }

    public static bool CandidateHasStatistics(string candidateText)
    {
        var c = candidateText.ToLowerInvariant();
        return CountKeywordHits(c, StatisticsKeywords) >= 1 && LargeNumberRegex().IsMatch(c);
    }

    public static int CountKeywordHitsForOncology(string text) =>
        CountKeywordHits(text.ToLowerInvariant(), OncologyKeywords);

    public static int CalculateMatchScore(string headline, string newsText, string candidateText)
    {
        var query = $"{headline} {newsText}";
        var terms = ExtractSearchTerms(headline, newsText);
        if (terms.Count == 0)
        {
            return 0;
        }

        var content = candidateText.ToLowerInvariant();
        var matched = terms.Count(t => content.Contains(t, StringComparison.Ordinal));
        var tokenPct = (int)Math.Round((double)matched / terms.Count * 100);

        var topic = DetectDominantTopic(headline, newsText);
        var penalty = 0;
        var bonus = 0;

        if (topic == NewsTopic.Oncology)
        {
            if (IsPrimarilyRespiratory(content))
            {
                penalty += 50;
            }

            if (CountKeywordHits(content, OncologyKeywords) >= 2)
            {
                bonus += 10;
            }

            if (QueryExpectsStatistics(headline, newsText) && CandidateHasStatistics(content))
            {
                bonus += 30;
            }
            else if (QueryExpectsStatistics(headline, newsText) && !CandidateHasStatistics(content))
            {
                penalty += 25;
            }
        }
        else if (topic == NewsTopic.Respiratory)
        {
            if (CountKeywordHits(content, RespiratoryKeywords) >= 2)
            {
                bonus += 15;
            }

            if (CountKeywordHits(content, OncologyKeywords) >= 2 && !IsPrimarilyRespiratory(content))
            {
                penalty += 20;
            }
        }

        return Math.Clamp(tokenPct + bonus - penalty, 0, 100);
    }

    private static int CountKeywordHits(string text, string[] keywords) =>
        keywords.Count(k => text.Contains(k, StringComparison.Ordinal));

    private static IEnumerable<(string Word, int Weight)> Tokenize(string text, int weight)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var raw in text.ToLowerInvariant().Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw.Trim();
            if (word.Length < 2)
            {
                continue;
            }

            if (word.Length <= 3 && !MedicalShortTerms.Contains(word))
            {
                continue;
            }

            if (StopWords.Contains(word))
            {
                continue;
            }

            if (word.All(char.IsDigit))
            {
                continue;
            }

            yield return (word, weight);
        }
    }

    [GeneratedRegex(@"\b\d{4,}\b")]
    private static partial Regex LargeNumberRegex();
}
