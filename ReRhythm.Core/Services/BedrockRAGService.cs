using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace ReRhythm.Core.Services;

public class BedrockRAGService
{
    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly IConfiguration _config;
    private readonly ILogger<BedrockRAGService> _logger;

    private string ModelId => _config["ReRhythm:BedrockModelId"] ?? "us.anthropic.claude-sonnet-4-20250514-v1:0";
    private int MaxTokens => int.TryParse(_config["ReRhythm:MaxTokens"], out var v) ? v : 4096;

    public BedrockRAGService(IAmazonBedrockRuntime bedrockRuntime, IConfiguration config, ILogger<BedrockRAGService> logger)
    {
        _bedrockRuntime = bedrockRuntime;
        _config = config;
        _logger = logger;
    }

    public async Task<RAGResponse> RetrieveAndGenerateAsync(string userQuery, string resumeContext, string targetRole, string industry, int yearsOfExperience, CancellationToken ct = default)
    {
        var prompt = BuildAugmentedQuery(userQuery, resumeContext, targetRole, industry, yearsOfExperience);
        _logger.LogInformation("Invoking Claude - Role: {Role}, Industry: {Industry}, Experience: {Years} years, ModelId: {ModelId}", 
            targetRole, industry, yearsOfExperience, ModelId);

        var requestBody = JsonSerializer.Serialize(new
        {
            anthropic_version = "bedrock-2023-05-31",
            max_tokens = MaxTokens,
            system = "You are a career coaching AI. Respond with valid JSON only.",
            messages = new[] { new { role = "user", content = prompt } }
        });

        try
        {
            var response = await _bedrockRuntime.InvokeModelAsync(new InvokeModelRequest
            {
                ModelId = ModelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(requestBody))
            }, ct);

            var responseJson = await new StreamReader(response.Body).ReadToEndAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var generatedText = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;

            _logger.LogInformation("Claude invocation succeeded for role: {Role}", targetRole);
            return new RAGResponse { GeneratedText = generatedText, Citations = [], SessionId = null };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Claude invocation failed for query: {Query}", userQuery);
            throw;
        }
    }

    public Task<List<RetrievedChunk>> RetrieveTopKChunksAsync(string query, CancellationToken ct = default)
    {
        _logger.LogWarning("RetrieveTopKChunksAsync called in DIRECT mode. Returning empty.");
        return Task.FromResult(new List<RetrievedChunk>());
    }

    private string BuildAugmentedQuery(string userQuery, string resumeContext, string targetRole, string industry, int yearsOfExperience)
    {
        var experienceLevel = yearsOfExperience <= 2 ? "Junior" : yearsOfExperience <= 5 ? "Mid-level" : yearsOfExperience <= 10 ? "Senior" : "Leadership/Executive";
        
        return "You are a career coaching AI. Respond with valid JSON only.\n\n" +
               $"TARGET ROLE: {targetRole}\n" +
               $"INDUSTRY: {industry}\n" +
               $"YEARS OF EXPERIENCE: {yearsOfExperience} ({experienceLevel})\n\n" +
               $"USER RESUME:\n{resumeContext}\n\n" +
               $"TASK:\n{userQuery}\n" +
               $"Tailor the roadmap to {experienceLevel} level with industry-specific skills for {industry}.\n\n" +
               "Respond ONLY with JSON:\n" +
               "{\n" +
               "  \"skillsIdentified\": [\"skill1\"],\n" +
               "  \"skillsToAcquire\": [\"skill1\"],\n" +
               "  \"modules\": [{\n" +
               "    \"weekNumber\": 1,\n" +
               "    \"theme\": \"Week theme\",\n" +
               "    \"milestonesUnlocked\": [\"milestone1\"],\n" +
               "    \"portfolioProject\": \"Project\",\n" +
               "    \"dailySprints\": [{\n" +
               "      \"day\": 1,\n" +
               "      \"topic\": \"Topic\",\n" +
               "      \"lessonFormat\": \"video\",\n" +
               "      \"estimatedMinutes\": 15,\n" +
               "      \"resourceRef\": \"URL\"\n" +
               "    }]\n" +
               "  }]\n" +
               "}";
    }
}

public record RAGResponse
{
    public string GeneratedText { get; init; } = string.Empty;
    public List<CitationSource> Citations { get; init; } = [];
    public string? SessionId { get; init; }
}

public record CitationSource
{
    public string Content { get; init; } = string.Empty;
    public string SourceUri { get; init; } = string.Empty;
}

public record RetrievedChunk
{
    public string Text { get; init; } = string.Empty;
    public double Score { get; init; }
    public string SourceUri { get; init; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = [];
}
