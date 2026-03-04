using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text.Json;

namespace ReRhythm.Web.Controllers;

public class SimulatorController : Controller
{
    private readonly DynamoDbService _dynamoDb;
    private readonly IAmazonBedrockRuntime _bedrockRuntime;
    private readonly IConfiguration _config;

    public SimulatorController(DynamoDbService dynamoDb, IAmazonBedrockRuntime bedrockRuntime, IConfiguration config)
    {
        _dynamoDb = dynamoDb;
        _bedrockRuntime = bedrockRuntime;
        _config = config;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
            return RedirectToAction("Upload", "Resume");

        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan == null)
            return RedirectToAction("Upload", "Resume");

        // Gold tier check
        if (plan.SubscriptionTier != "Gold")
            return RedirectToAction("Upgrade", "Premium");

        ViewBag.UserId = userId;
        ViewBag.TargetRole = plan.TargetRole;
        ViewBag.SubscriptionTier = plan.SubscriptionTier ?? "Basic";
        
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> GenerateScenario(string userId, string scenarioType, CancellationToken ct)
    {
        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan == null)
            return Json(new { success = false, error = "User not found" });

        var prompt = $@"Generate an interactive {scenarioType} scenario for a {plan.TargetRole} role in {plan.Industry}.

Create a scenario with:
1. A realistic situation (2-3 sentences)
2. A specific challenge or decision to make
3. Exactly 4 multiple choice options (A, B, C, D)
4. Each option should be a different approach/response
5. One option should be clearly the best choice

Format EXACTLY as:

**Scenario:**
[Situation description]

**Challenge:**
[What decision needs to be made]

**Options:**
A) [First option]
B) [Second option]
C) [Third option]
D) [Fourth option]

**Correct Answer:** [A/B/C/D]

**Explanation:**
[Why the correct answer is best and what's wrong with other options]";

        var modelId = _config["ReRhythm:BedrockModelId"] ?? "us.anthropic.claude-sonnet-4-20250514-v1:0";
        var requestBody = JsonSerializer.Serialize(new
        {
            anthropic_version = "bedrock-2023-05-31",
            max_tokens = 2000,
            system = "You are a career coaching AI that generates realistic workplace scenarios. Respond with formatted text only, not JSON.",
            messages = new[] { new { role = "user", content = prompt } }
        });

        var response = await _bedrockRuntime.InvokeModelAsync(new InvokeModelRequest
        {
            ModelId = modelId,
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(requestBody))
        }, ct);

        var responseJson = await new StreamReader(response.Body).ReadToEndAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var generatedText = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;

        return Json(new { success = true, scenario = generatedText });
    }
}
