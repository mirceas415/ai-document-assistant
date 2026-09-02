using System.Text.Json.Serialization;

namespace AI.DocumentAssistant.Server.Models;

/// <summary>
/// Describes the structural representation detected in a PDF without making a
/// semantic claim about the document's content.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TechnicalType
{
    /// <summary>There is not enough structural evidence for a safe classification.</summary>
    Unknown,

    /// <summary>A meaningful text layer is the primary useful representation.</summary>
    TextBased,

    /// <summary>
    /// Little or no meaningful text exists and a raster image covers most of the page,
    /// which is structurally consistent with a scan.
    /// </summary>
    Scanned,

    /// <summary>
    /// Little or no meaningful text exists and raster images are present, but no image
    /// satisfies the conservative page-sized scan threshold.
    /// </summary>
    ImageBased,

    /// <summary>
    /// Meaningful text and substantial raster content coexist, or materially different
    /// useful page representations coexist in the document.
    /// </summary>
    Mixed
}
