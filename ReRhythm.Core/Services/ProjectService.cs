using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using ReRhythm.Core.Models;

namespace ReRhythm.Core.Services;

public class ProjectService
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly IAmazonS3 _s3;
    private readonly ILogger<ProjectService> _logger;
    private readonly string _projectsTable;
    private readonly string _reviewsTable;
    private readonly string _bucketName;
    private readonly string _region;

    public ProjectService(
        IAmazonDynamoDB dynamoDb,
        IAmazonS3 s3,
        ILogger<ProjectService> logger,
        Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _dynamoDb = dynamoDb;
        _s3 = s3;
        _logger = logger;
        _projectsTable = config["ReRhythm:ProjectsTable"] ?? "rerythm-prod-projects";
        _reviewsTable = config["ReRhythm:ProjectReviewsTable"] ?? "rerythm-prod-project-reviews";
        _bucketName = config["ReRhythm:ProjectImagesBucket"] ?? "rerythm-prod-project-images-250025622388";
        _region = config["AWS:Region"] ?? "us-east-1";
    }

    public async Task<string> UploadImageAsync(Stream imageStream, string fileName, CancellationToken ct)
    {
        imageStream.Position = 0;
        var extension = Path.GetExtension(fileName);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var sanitizedName = System.Text.RegularExpressions.Regex.Replace(nameWithoutExt, @"[^a-zA-Z0-9]+", "_").Trim('_');
        var key = $"projects/{Guid.NewGuid()}/{sanitizedName}{extension}";
        
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = imageStream,
            ContentType = extension.ToLower() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            }
        }, ct);

        return $"https://{_bucketName}.s3.{_region}.amazonaws.com/{key}";
    }

    public async Task<string> CreateProjectAsync(Project project, CancellationToken ct)
    {
        await _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _projectsTable,
            Item = new Dictionary<string, AttributeValue>
            {
                ["ProjectId"] = new AttributeValue { S = project.ProjectId },
                ["UserId"] = new AttributeValue { S = project.UserId },
                ["UserName"] = new AttributeValue { S = project.UserName },
                ["Title"] = new AttributeValue { S = project.Title },
                ["Description"] = new AttributeValue { S = project.Description },
                ["TechStack"] = new AttributeValue { S = project.TechStack },
                ["GithubUrl"] = new AttributeValue { S = project.GithubUrl },
                ["LiveUrl"] = new AttributeValue { S = project.LiveUrl },
                ["ImageUrls"] = new AttributeValue { SS = project.ImageUrls.Any() ? project.ImageUrls : new List<string> { "placeholder" } },
                ["Industry"] = new AttributeValue { S = project.Industry },
                ["CreatedAt"] = new AttributeValue { S = project.CreatedAt.ToString("o") },
                ["ViewCount"] = new AttributeValue { N = "0" },
                ["ReviewCount"] = new AttributeValue { N = "0" },
                ["AverageRating"] = new AttributeValue { N = "0" }
            }
        }, ct);

        return project.ProjectId;
    }

    public async Task<List<Project>> GetProjectsByIndustryAsync(string industry, CancellationToken ct)
    {
        var response = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _projectsTable,
            IndexName = "Industry-CreatedAt-Index",
            KeyConditionExpression = "Industry = :industry",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":industry"] = new AttributeValue { S = industry }
            },
            ScanIndexForward = false,
            Limit = 50
        }, ct);

        return response.Items.Select(item => new Project
        {
            ProjectId = item["ProjectId"].S,
            UserId = item["UserId"].S,
            UserName = item["UserName"].S,
            Title = item["Title"].S,
            Description = item["Description"].S,
            TechStack = item["TechStack"].S,
            GithubUrl = item["GithubUrl"].S,
            LiveUrl = item["LiveUrl"].S,
            ImageUrls = item["ImageUrls"].SS.Where(s => s != "placeholder").ToList(),
            Industry = item["Industry"].S,
            CreatedAt = DateTime.Parse(item["CreatedAt"].S),
            ViewCount = int.Parse(item["ViewCount"].N),
            ReviewCount = int.Parse(item["ReviewCount"].N),
            AverageRating = double.Parse(item["AverageRating"].N)
        }).ToList();
    }

    public async Task<Project?> GetProjectAsync(string projectId, CancellationToken ct)
    {
        var response = await _dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = _projectsTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["ProjectId"] = new AttributeValue { S = projectId }
            }
        }, ct);

        if (!response.IsItemSet) return null;

        var item = response.Item;
        return new Project
        {
            ProjectId = item["ProjectId"].S,
            UserId = item["UserId"].S,
            UserName = item["UserName"].S,
            Title = item["Title"].S,
            Description = item["Description"].S,
            TechStack = item["TechStack"].S,
            GithubUrl = item["GithubUrl"].S,
            LiveUrl = item["LiveUrl"].S,
            ImageUrls = item["ImageUrls"].SS.Where(s => s != "placeholder").ToList(),
            Industry = item["Industry"].S,
            CreatedAt = DateTime.Parse(item["CreatedAt"].S),
            ViewCount = int.Parse(item["ViewCount"].N),
            ReviewCount = int.Parse(item["ReviewCount"].N),
            AverageRating = double.Parse(item["AverageRating"].N)
        };
    }

    public async Task IncrementViewCountAsync(string projectId, CancellationToken ct)
    {
        await _dynamoDb.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _projectsTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["ProjectId"] = new AttributeValue { S = projectId }
            },
            UpdateExpression = "ADD ViewCount :inc",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":inc"] = new AttributeValue { N = "1" }
            }
        }, ct);
    }

    public async Task<string> AddReviewAsync(ProjectReview review, CancellationToken ct)
    {
        await _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _reviewsTable,
            Item = new Dictionary<string, AttributeValue>
            {
                ["ReviewId"] = new AttributeValue { S = review.ReviewId },
                ["ProjectId"] = new AttributeValue { S = review.ProjectId },
                ["ReviewerId"] = new AttributeValue { S = review.ReviewerId },
                ["ReviewerName"] = new AttributeValue { S = review.ReviewerName },
                ["Rating"] = new AttributeValue { N = review.Rating.ToString() },
                ["Comment"] = new AttributeValue { S = review.Comment },
                ["CreatedAt"] = new AttributeValue { S = review.CreatedAt.ToString("o") }
            }
        }, ct);

        await UpdateProjectRatingAsync(review.ProjectId, ct);
        return review.ReviewId;
    }

    public async Task<List<ProjectReview>> GetReviewsAsync(string projectId, CancellationToken ct)
    {
        var response = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _reviewsTable,
            IndexName = "ProjectId-CreatedAt-Index",
            KeyConditionExpression = "ProjectId = :pid",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pid"] = new AttributeValue { S = projectId }
            },
            ScanIndexForward = false
        }, ct);

        return response.Items.Select(item => new ProjectReview
        {
            ReviewId = item["ReviewId"].S,
            ProjectId = item["ProjectId"].S,
            ReviewerId = item["ReviewerId"].S,
            ReviewerName = item["ReviewerName"].S,
            Rating = int.Parse(item["Rating"].N),
            Comment = item["Comment"].S,
            CreatedAt = DateTime.Parse(item["CreatedAt"].S)
        }).ToList();
    }

    private async Task UpdateProjectRatingAsync(string projectId, CancellationToken ct)
    {
        var reviews = await GetReviewsAsync(projectId, ct);
        var avgRating = reviews.Any() ? reviews.Average(r => r.Rating) : 0;

        await _dynamoDb.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _projectsTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["ProjectId"] = new AttributeValue { S = projectId }
            },
            UpdateExpression = "SET AverageRating = :rating, ReviewCount = :count",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":rating"] = new AttributeValue { N = avgRating.ToString("F1") },
                [":count"] = new AttributeValue { N = reviews.Count.ToString() }
            }
        }, ct);
    }

    public async Task<int> GetUserReputationAsync(string userId, CancellationToken ct)
    {
        var projectsResponse = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _projectsTable,
            IndexName = "UserId-CreatedAt-Index",
            KeyConditionExpression = "UserId = :uid",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":uid"] = new AttributeValue { S = userId }
            }
        }, ct);

        var projects = projectsResponse.Items.Select(item => new
        {
            ReviewCount = int.Parse(item["ReviewCount"].N),
            AverageRating = double.Parse(item["AverageRating"].N)
        }).ToList();

        var reputation = projects.Sum(p => (int)(p.ReviewCount * p.AverageRating * 10));
        return reputation;
    }
}
