using MedicalNewsVerifier.Web.Models;

namespace MedicalNewsVerifier.Web.Services.Parsers;

public sealed class SourceParserRegistry(IEnumerable<ISourceParser> parsers)
{
    public ISourceParser? FindParser(TrustedSource source) =>
        parsers.FirstOrDefault(p => p.CanParse(source));

    public ISourceParser? FindParserForUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return parsers.FirstOrDefault(p =>
        {
            try
            {
                var host = new Uri(url, UriKind.Absolute).Host;
                return p.CanParse(new TrustedSource { BaseUrl = url, Name = host });
            }
            catch (UriFormatException)
            {
                return false;
            }
        });
    }
}
