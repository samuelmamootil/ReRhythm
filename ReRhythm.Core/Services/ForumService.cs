using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Microsoft.Extensions.Logging;
using ReRhythm.Core.Models;

namespace ReRhythm.Core.Services;

public class ForumService
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly IAmazonS3 _s3;
    private readonly IAmazonRekognition _rekognition;
    private readonly IAmazonSimpleEmailService _ses;
    private readonly DynamoDbService _dynamoDbService;
    private readonly ILogger<ForumService> _logger;
    private readonly string _questionsTable;
    private readonly string _answersTable;
    private readonly string _bucketName;

    public ForumService(
        IAmazonDynamoDB dynamoDb,
        IAmazonS3 s3,
        IAmazonRekognition rekognition,
        IAmazonSimpleEmailService ses,
        DynamoDbService dynamoDbService,
        ILogger<ForumService> logger,
        Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _dynamoDb = dynamoDb;
        _s3 = s3;
        _rekognition = rekognition;
        _ses = ses;
        _dynamoDbService = dynamoDbService;
        _logger = logger;
        _questionsTable = config["ReRhythm:ForumQuestionsTable"] ?? "rerythm-prod-forum-questions";
        _answersTable = config["ReRhythm:ForumAnswersTable"] ?? "rerythm-prod-forum-answers";
        _bucketName = config["ReRhythm:ForumImagesBucket"] ?? "rerythm-prod-forum-images-250025622388";
    }

    public async Task<(bool IsAppropriate, string Reason)> ModerateImageAsync(Stream imageStream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await imageStream.CopyToAsync(ms, ct);
        ms.Position = 0;

        var request = new DetectModerationLabelsRequest
        {
            Image = new Image { Bytes = ms },
            MinConfidence = 60F
        };

        var response = await _rekognition.DetectModerationLabelsAsync(request, ct);
        
        if (response.ModerationLabels.Any())
        {
            var labels = string.Join(", ", response.ModerationLabels.Select(l => l.Name));
            return (false, $"Inappropriate content detected: {labels}");
        }

        return (true, string.Empty);
    }

    public async Task<string> UploadImageAsync(Stream imageStream, string fileName, CancellationToken ct)
    {
        var key = $"forum/{Guid.NewGuid()}/{fileName}";
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = imageStream,
            ContentType = "image/jpeg"
        }, ct);

        return $"https://{_bucketName}.s3.amazonaws.com/{key}";
    }

    public async Task<string> PostQuestionAsync(ForumQuestion question, CancellationToken ct)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["QuestionId"] = new AttributeValue { S = question.QuestionId },
            ["UserId"] = new AttributeValue { S = question.UserId },
            ["UserName"] = new AttributeValue { S = question.UserName },
            ["Industry"] = new AttributeValue { S = question.Industry },
            ["Title"] = new AttributeValue { S = question.Title },
            ["Content"] = new AttributeValue { S = question.Content },
            ["CreatedAt"] = new AttributeValue { S = question.CreatedAt.ToString("o") },
            ["ViewCount"] = new AttributeValue { N = "0" }
        };

        if (question.ImageUrls.Any())
            item["ImageUrls"] = new AttributeValue { SS = question.ImageUrls };

        await _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _questionsTable,
            Item = item
        }, ct);

        _ = Task.Run(() => NotifyUsersAsync(question.Industry, question.Title, question.QuestionId, ct));

        return question.QuestionId;
    }

    public async Task<string> PostAnswerAsync(ForumAnswer answer, CancellationToken ct)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["AnswerId"] = new AttributeValue { S = answer.AnswerId },
            ["QuestionId"] = new AttributeValue { S = answer.QuestionId },
            ["UserId"] = new AttributeValue { S = answer.UserId },
            ["UserName"] = new AttributeValue { S = answer.UserName },
            ["Content"] = new AttributeValue { S = answer.Content },
            ["CreatedAt"] = new AttributeValue { S = answer.CreatedAt.ToString("o") },
            ["Upvotes"] = new AttributeValue { N = "0" },
            ["Downvotes"] = new AttributeValue { N = "0" }
        };

        await _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _answersTable,
            Item = item
        }, ct);

        return answer.AnswerId;
    }

    public async Task<List<ForumQuestion>> GetQuestionsByIndustryAsync(string industry, CancellationToken ct)
    {
        var response = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _questionsTable,
            IndexName = "Industry-CreatedAt-Index",
            KeyConditionExpression = "Industry = :industry",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":industry"] = new AttributeValue { S = industry }
            },
            ScanIndexForward = false,
            Limit = 50
        }, ct);

        return response.Items.Select(item => new ForumQuestion
        {
            QuestionId = item["QuestionId"].S,
            UserId = item["UserId"].S,
            UserName = item["UserName"].S,
            Industry = item["Industry"].S,
            Title = item["Title"].S,
            Content = item["Content"].S,
            CreatedAt = DateTime.Parse(item["CreatedAt"].S),
            AcceptedAnswerId = item.ContainsKey("AcceptedAnswerId") ? item["AcceptedAnswerId"].S : null,
            ViewCount = int.Parse(item["ViewCount"].N),
            ImageUrls = item.ContainsKey("ImageUrls") ? item["ImageUrls"].SS : new List<string>()
        }).ToList();
    }

    public async Task<ForumQuestion?> GetQuestionAsync(string questionId, CancellationToken ct)
    {
        var response = await _dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = _questionsTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["QuestionId"] = new AttributeValue { S = questionId }
            }
        }, ct);

        if (!response.IsItemSet) return null;

        var item = response.Item;
        return new ForumQuestion
        {
            QuestionId = item["QuestionId"].S,
            UserId = item["UserId"].S,
            UserName = item["UserName"].S,
            Industry = item["Industry"].S,
            Title = item["Title"].S,
            Content = item["Content"].S,
            CreatedAt = DateTime.Parse(item["CreatedAt"].S),
            AcceptedAnswerId = item.ContainsKey("AcceptedAnswerId") ? item["AcceptedAnswerId"].S : null,
            ViewCount = int.Parse(item["ViewCount"].N),
            ImageUrls = item.ContainsKey("ImageUrls") ? item["ImageUrls"].SS : new List<string>()
        };
    }

    public async Task<List<ForumAnswer>> GetAnswersAsync(string questionId, CancellationToken ct)
    {
        var response = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _answersTable,
            IndexName = "QuestionId-CreatedAt-Index",
            KeyConditionExpression = "QuestionId = :qid",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":qid"] = new AttributeValue { S = questionId }
            }
        }, ct);

        return response.Items.Select(item => new ForumAnswer
        {
            AnswerId = item["AnswerId"].S,
            QuestionId = item["QuestionId"].S,
            UserId = item["UserId"].S,
            UserName = item["UserName"].S,
            Content = item["Content"].S,
            CreatedAt = DateTime.Parse(item["CreatedAt"].S),
            Upvotes = int.Parse(item["Upvotes"].N),
            Downvotes = int.Parse(item["Downvotes"].N),
            UpvotedBy = item.ContainsKey("UpvotedBy") ? item["UpvotedBy"].SS : new List<string>(),
            DownvotedBy = item.ContainsKey("DownvotedBy") ? item["DownvotedBy"].SS : new List<string>()
        }).ToList();
    }

    public async Task VoteAnswerAsync(string answerId, string userId, bool isUpvote, CancellationToken ct)
    {
        var voteAttr = isUpvote ? "UpvotedBy" : "DownvotedBy";
        var counterAttr = isUpvote ? "Upvotes" : "Downvotes";

        await _dynamoDb.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _answersTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["AnswerId"] = new AttributeValue { S = answerId }
            },
            UpdateExpression = $"ADD {voteAttr} :userId, {counterAttr} :inc",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":userId"] = new AttributeValue { SS = new List<string> { userId } },
                [":inc"] = new AttributeValue { N = "1" }
            }
        }, ct);
    }

    public async Task AcceptAnswerAsync(string questionId, string answerId, CancellationToken ct)
    {
        await _dynamoDb.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _questionsTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["QuestionId"] = new AttributeValue { S = questionId }
            },
            UpdateExpression = "SET AcceptedAnswerId = :aid",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":aid"] = new AttributeValue { S = answerId }
            }
        }, ct);
    }

    private async Task NotifyUsersAsync(string industry, string questionTitle, string questionId, CancellationToken ct)
    {
        try
        {
            var scanResponse = await _dynamoDb.ScanAsync(new ScanRequest
            {
                TableName = "rerythm-roadmaps",
                FilterExpression = "Industry = :industry",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":industry"] = new AttributeValue { S = industry }
                },
                ProjectionExpression = "UserId",
                Limit = 10
            }, ct);

            foreach (var item in scanResponse.Items)
            {
                try
                {
                    var userId = item["UserId"].S;
                    var plan = await _dynamoDbService.GetLatestRoadmapAsync(userId, ct);
                    if (plan?.ContactInfo != null && plan.ContactInfo.Contains("@"))
                    {
                        var email = System.Text.RegularExpressions.Regex.Match(plan.ContactInfo, @"[\w\.-]+@[\w\.-]+\.\w+").Value;
                        if (!string.IsNullOrEmpty(email))
                        {
                            await SendEmailAsync(email, questionTitle, questionId, ct);
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify users");
        }
    }

    private async Task SendEmailAsync(string toEmail, string questionTitle, string questionId, CancellationToken ct)
    {
        try
        {
            await _ses.SendEmailAsync(new SendEmailRequest
            {
                Source = "noreply@rerythm.com",
                Destination = new Destination { ToAddresses = new List<string> { toEmail } },
                Message = new Message
                {
                    Subject = new Content($"New Question: {questionTitle}"),
                    Body = new Body
                    {
                        Html = new Content($"<p>New question in your industry:</p><h3>{questionTitle}</h3><p><a href='https://rerythm.com/Forum/Question?id={questionId}'>View</a></p>")
                    }
                }
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send email to {Email}", toEmail);
        }
    }
}
