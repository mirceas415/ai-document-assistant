using System.Security.Cryptography;
using System.Text;
using AI.DocumentAssistant.Server.Models;

namespace AI.DocumentAssistant.Server.Ocr;

public sealed class OcrRoutingPolicy
{
    public string Version => OcrArchitecture.RoutingVersion;

    public bool ShouldOcr(TechnicalType technicalType) =>
        technicalType == TechnicalType.Scanned;

    public IReadOnlyList<DocumentPageTechnicalAnalysis> SelectCandidates(
        IEnumerable<DocumentPageTechnicalAnalysis> pages) =>
        pages.Where(page => ShouldOcr(page.TechnicalType))
            .OrderBy(page => page.PageNumber)
            .ToArray();

    public string ComputeRoutingHash(
        IEnumerable<DocumentPageTechnicalAnalysis> selectedPages)
    {
        var routing = string.Join(
            '\n',
            selectedPages.OrderBy(page => page.PageNumber)
                .Select(page => $"{page.PageNumber}:{page.TechnicalType}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(routing)));
    }
}
