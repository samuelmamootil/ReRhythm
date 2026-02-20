using ReRhythm.Core.Services;

namespace ReRhythm.Core.Models;

/// <summary>
/// Top-level response returned by the RAG pipeline for roadmap generation.
/// Wraps the generated plan + citations from Bedrock Knowledge Base.
/// </summary>
public class RoadmapResponse
{
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public RoadmapPlan? Plan { get; set; }
    public List<CitationSource> Citations { get; set; } = [];
    public double ConfidenceScore { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
