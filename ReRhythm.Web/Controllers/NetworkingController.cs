using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;
using ReRhythm.Core.Models;

namespace ReRhythm.Web.Controllers;

public class NetworkingController : Controller
{
    private readonly NetworkingService _networkingService;
    private readonly DynamoDbService _dynamoDb;
    private readonly ILogger<NetworkingController> _logger;

    public NetworkingController(NetworkingService networkingService, DynamoDbService dynamoDb, ILogger<NetworkingController> logger)
    {
        _networkingService = networkingService;
        _dynamoDb = dynamoDb;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string userId, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Upload", "Resume");

            var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
            if (plan == null) return RedirectToAction("Upload", "Resume");

            var members = await _networkingService.GetMembersByIndustryAsync(plan.Industry, userId, ct);
            var connections = await _networkingService.GetConnectionsAsync(userId, ct);
            var pendingRequests = await _networkingService.GetPendingRequestsAsync(userId, ct);

            ViewBag.UserId = userId;
            ViewBag.UserName = plan.FullName?.Split('\n')[0]?.Trim() ?? "User";
            ViewBag.Industry = plan.Industry;
            ViewBag.TargetRole = plan.TargetRole;
            ViewBag.Connections = connections;
            ViewBag.PendingCount = pendingRequests.Count;
            
            return View(members);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading networking page for user {UserId}", userId);
            TempData["Error"] = "Unable to load networking page. Please try again.";
            return RedirectToAction("Index", "Community", new { userId });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SendRequest(string fromUserId, string toUserId, string message, CancellationToken ct)
    {
        try
        {
            var request = new ConnectionRequest
            {
                FromUserId = fromUserId,
                ToUserId = toUserId,
                Message = message ?? ""
            };

            await _networkingService.SendConnectionRequestAsync(request, ct);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending connection request");
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Requests(string userId, CancellationToken ct)
    {
        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan == null) return RedirectToAction("Upload", "Resume");

        var requests = await _networkingService.GetPendingRequestsAsync(userId, ct);
        
        var requestsWithProfiles = new List<(ConnectionRequest Request, UserProfile Profile)>();
        foreach (var req in requests)
        {
            var fromPlan = await _dynamoDb.GetLatestRoadmapAsync(req.FromUserId, ct);
            if (fromPlan != null)
            {
                requestsWithProfiles.Add((req, new UserProfile
                {
                    UserId = req.FromUserId,
                    Name = fromPlan.FullName?.Split('\n')[0]?.Trim() ?? "User",
                    TargetRole = fromPlan.TargetRole,
                    Industry = fromPlan.Industry
                }));
            }
        }

        ViewBag.UserId = userId;
        ViewBag.UserName = plan.FullName?.Split('\n')[0]?.Trim() ?? "User";
        
        return View(requestsWithProfiles);
    }

    [HttpPost]
    public async Task<IActionResult> AcceptRequest(string connectionId, CancellationToken ct)
    {
        await _networkingService.UpdateConnectionStatusAsync(connectionId, "Accepted", ct);
        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> RejectRequest(string connectionId, CancellationToken ct)
    {
        await _networkingService.UpdateConnectionStatusAsync(connectionId, "Rejected", ct);
        return Json(new { success = true });
    }
}
