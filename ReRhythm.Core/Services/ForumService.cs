using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.Comprehend;
using Amazon.Comprehend.Model;
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
    private readonly IAmazonComprehend _comprehend;
    private readonly IAmazonSimpleEmailService _ses;
    private readonly DynamoDbService _dynamoDbService;
    private readonly ILogger<ForumService> _logger;
    private readonly string _questionsTable;
    private readonly string _answersTable;
    private readonly string _notificationsTable;
    private readonly string _bucketName;
    private readonly string _region;

    public ForumService(
        IAmazonDynamoDB dynamoDb,
        IAmazonS3 s3,
        IAmazonRekognition rekognition,
        IAmazonComprehend comprehend,
        IAmazonSimpleEmailService ses,
        DynamoDbService dynamoDbService,
        ILogger<ForumService> logger,
        Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _dynamoDb = dynamoDb;
        _s3 = s3;
        _rekognition = rekognition;
        _comprehend = comprehend;
        _ses = ses;
        _dynamoDbService = dynamoDbService;
        _logger = logger;
        _questionsTable = config["ReRhythm:ForumQuestionsTable"] ?? "rerythm-prod-forum-questions";
        _answersTable = config["ReRhythm:ForumAnswersTable"] ?? "rerythm-prod-forum-answers";
        _notificationsTable = config["ReRhythm:ForumNotificationsTable"] ?? "rerythm-prod-forum-notifications";
        _bucketName = config["ReRhythm:ForumImagesBucket"] ?? "rerythm-prod-forum-images-250025622388";
        _region = config["AWS:Region"] ?? "us-east-1";
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

    public async Task<(bool IsAppropriate, string Reason)> ModerateTextAsync(string text, CancellationToken ct)
    {
        // Quick fallback check first (faster)
        var (fallbackOk, fallbackReason) = ModerateTextFallback(text);
        if (!fallbackOk)
        {
            _logger.LogWarning("Text moderation failed (fallback): {Reason}", fallbackReason);
            return (false, fallbackReason);
        }

        // Then AWS Comprehend for deeper analysis
        try
        {
            var request = new DetectToxicContentRequest
            {
                TextSegments = new List<TextSegment> { new TextSegment { Text = text } },
                LanguageCode = LanguageCode.En
            };

            var response = await _comprehend.DetectToxicContentAsync(request, ct);
            
            foreach (var result in response.ResultList)
            {
                var toxicLabels = result.Labels.Where(l => l.Score > 0.5f).Select(l => l.Name).ToList();
                if (toxicLabels.Any())
                {
                    var reason = $"Inappropriate content detected: {string.Join(", ", toxicLabels)}";
                    _logger.LogWarning("Text moderation failed (AWS): {Reason}", reason);
                    return (false, reason);
                }
            }
            
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Comprehend moderation failed, using fallback result");
            return (true, string.Empty); // Already passed fallback
        }
    }

    private (bool IsAppropriate, string Reason) ModerateTextFallback(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (false, "Empty content");
            
        var lowerText = text.ToLower();
        
        // Remove emojis and special characters for checking
        var cleanText = System.Text.RegularExpressions.Regex.Replace(lowerText, @"[^a-z0-9\s]", " ");
        
        // Profanity and offensive language - check anywhere in text
        var offensiveWords = new[] { "fuck", "shit", "bitch", "asshole", "damn", "crap", "bastard", "dick", "pussy", "cock", "nigger", "nigga", "faggot", "retard", "cunt", "whore", "slut", "suck", "dumb" };
        foreach (var word in offensiveWords)
        {
            if (cleanText.Contains(word))
            {
                _logger.LogWarning("Offensive word detected: {Word}", word);
                return (false, "Inappropriate language detected");
            }
        }
        
        // Violence and threats
        var violenceWords = new[] { "kill you", "murder you", "die", "death to", "shoot you", "bomb", "attack you", "hurt you", "harm you", "beat you" };
        foreach (var phrase in violenceWords)
        {
            if (cleanText.Contains(phrase))
            {
                _logger.LogWarning("Violent phrase detected: {Phrase}", phrase);
                return (false, "Violent or threatening content detected");
            }
        }
        
        // Hate speech and discrimination
        var hatePatterns = new[]
        {
            "are dumb", "are stupid", "are idiots", "are trash", "are worthless",
            "should die", "deserve to die", "go kill yourself", "kys",
            "i hate", "hate all", "hate people who", "you suck", "guys suck"
        };
        
        foreach (var pattern in hatePatterns)
        {
            if (cleanText.Contains(pattern))
            {
                _logger.LogWarning("Hate speech pattern detected: {Pattern}", pattern);
                return (false, "Hate speech or discrimination detected");
            }
        }
        
        // Spam indicators
        if (text.Count(c => c == '!') > 5 || text.Count(c => c == '?') > 5)
        {
            _logger.LogWarning("Excessive punctuation detected");
            return (false, "Excessive punctuation detected");
        }
        
        if (System.Text.RegularExpressions.Regex.Matches(text, @"http[s]?://").Count > 3)
        {
            _logger.LogWarning("Too many links detected");
            return (false, "Too many links detected");
        }
        
        if (text.Length > 20 && text.Count(char.IsUpper) > text.Length * 0.7)
        {
            _logger.LogWarning("Excessive capitalization detected");
            return (false, "Excessive capitalization detected");
        }
        
        return (true, string.Empty);
    }

    public async Task<string> UploadImageAsync(Stream imageStream, string fileName, CancellationToken ct)
    {
        imageStream.Position = 0;
        
        var extension = Path.GetExtension(fileName);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var sanitizedName = System.Text.RegularExpressions.Regex.Replace(nameWithoutExt, @"[^a-zA-Z0-9]+", "_").Trim('_');
        var sanitizedFileName = sanitizedName + extension;
        var key = $"forum/{Guid.NewGuid()}/{sanitizedFileName}";
        
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = imageStream,
            ContentType = GetContentType(extension)
        }, ct);

        return $"https://{_bucketName}.s3.{_region}.amazonaws.com/{key}";
    }
    
    private string GetContentType(string extension)
    {
        return extension.ToLower() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
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

        // Notify question author
        try
        {
            var question = await GetQuestionAsync(answer.QuestionId, ct);
            _logger.LogInformation("Question found: {QuestionId}, Author: {UserId}, Answerer: {AnswerUserId}", 
                question?.QuestionId, question?.UserId, answer.UserId);
            
            if (question != null && question.UserId != answer.UserId)
            {
                var notification = new ForumNotification
                {
                    UserId = question.UserId,
                    Type = "answer",
                    Message = $"{answer.UserName} answered your question: {question.Title}",
                    QuestionId = answer.QuestionId,
                    AnswerId = answer.AnswerId
                };
                
                _logger.LogInformation("Creating notification for user {UserId}: {Message}", 
                    notification.UserId, notification.Message);
                
                await CreateNotificationAsync(notification, ct);
                
                _logger.LogInformation("Notification created successfully");
            }
            else
            {
                _logger.LogInformation("Skipping notification - same user or question not found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create answer notification");
        }

        return answer.AnswerId;
    }

    public async Task<List<ForumQuestion>> GetAllQuestionsAsync(CancellationToken ct)
    {
        var response = await _dynamoDb.ScanAsync(new ScanRequest
        {
            TableName = _questionsTable,
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
        }).OrderByDescending(q => q.CreatedAt).ToList();
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

    public async Task IncrementViewCountAsync(string questionId, CancellationToken ct)
    {
        await _dynamoDb.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _questionsTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["QuestionId"] = new AttributeValue { S = questionId }
            },
            UpdateExpression = "ADD ViewCount :inc",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":inc"] = new AttributeValue { N = "1" }
            }
        }, ct);
    }

    public async Task DeleteQuestionAsync(string questionId, CancellationToken ct)
    {
        await _dynamoDb.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _questionsTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["QuestionId"] = new AttributeValue { S = questionId }
            }
        }, ct);
    }

    public async Task DeleteAnswerAsync(string answerId, CancellationToken ct)
    {
        await _dynamoDb.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _answersTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["AnswerId"] = new AttributeValue { S = answerId }
            }
        }, ct);
    }

    public async Task NotifyViolationAsync(string userId, string contentType, string reason, CancellationToken ct)
    {
        try
        {
            // Create in-app notification first
            await CreateNotificationAsync(new ForumNotification
            {
                UserId = userId,
                Type = "violation",
                Message = $"Your {contentType} was removed: {reason}"
            }, ct);
            
            _logger.LogInformation("Violation notification created for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create in-app notification for user {UserId}", userId);
        }

        // Send email
        _ = Task.Run(async () =>
        {
            try
            {
                var plan = await _dynamoDbService.GetLatestRoadmapAsync(userId, CancellationToken.None);
                if (plan == null || string.IsNullOrEmpty(plan.Email))
                    return;

                await _ses.SendEmailAsync(new SendEmailRequest
                {
                    Source = "mamootilsamuel1@gmail.com",
                    Destination = new Destination { ToAddresses = new List<string> { plan.Email } },
                    Message = new Message
                    {
                        Subject = new Content("ReRhythm Forum - Content Removed"),
                        Body = new Body
                        {
                            Html = new Content($@"
                                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
                                    <h2 style='color: #ef4444;'>Content Removed - Community Guidelines Violation</h2>
                                    <p>Hello {plan.FullName?.Split('\n')[0]?.Trim() ?? "User"},</p>
                                    <p>Your recent forum {contentType} has been removed for violating our community guidelines.</p>
                                    <div style='background: #fee; border-left: 4px solid #ef4444; padding: 15px; margin: 20px 0;'>
                                        <strong>Reason:</strong> {reason}
                                    </div>
                                    <p><strong>Our Community Guidelines:</strong></p>
                                    <ul>
                                        <li>Be respectful and professional</li>
                                        <li>No offensive language or harassment</li>
                                        <li>No spam or excessive self-promotion</li>
                                        <li>No inappropriate images or content</li>
                                    </ul>
                                    <p>Please review our guidelines before posting again. Repeated violations may result in account restrictions.</p>
                                    <p>If you believe this was a mistake, please reply to this email.</p>
                                    <hr style='margin: 30px 0; border: none; border-top: 1px solid #ddd;' />
                                    <p style='color: #666; font-size: 12px;'>ReRhythm Community Team</p>
                                </div>
                            ")
                        }
                    }
                }, CancellationToken.None);

                _logger.LogInformation("Violation email sent to {Email} for {ContentType}", plan.Email, contentType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send violation email to user {UserId}", userId);
            }
        });
    }

    public async Task CreateNotificationAsync(ForumNotification notification, CancellationToken ct)
    {
        await _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _notificationsTable,
            Item = new Dictionary<string, AttributeValue>
            {
                ["NotificationId"] = new AttributeValue { S = notification.NotificationId },
                ["UserId"] = new AttributeValue { S = notification.UserId },
                ["Type"] = new AttributeValue { S = notification.Type },
                ["Message"] = new AttributeValue { S = notification.Message },
                ["QuestionId"] = new AttributeValue { S = notification.QuestionId ?? "" },
                ["AnswerId"] = new AttributeValue { S = notification.AnswerId ?? "" },
                ["CreatedAt"] = new AttributeValue { S = notification.CreatedAt.ToString("o") },
                ["IsRead"] = new AttributeValue { BOOL = notification.IsRead }
            }
        }, ct);
    }

    public async Task<List<ForumNotification>> GetUserNotificationsAsync(string userId, CancellationToken ct)
    {
        try
        {
            var response = await _dynamoDb.QueryAsync(new QueryRequest
            {
                TableName = _notificationsTable,
                IndexName = "UserId-CreatedAt-Index",
                KeyConditionExpression = "UserId = :uid",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":uid"] = new AttributeValue { S = userId }
                },
                ScanIndexForward = false,
                Limit = 50
            }, ct);

            return response.Items.Select(item => new ForumNotification
            {
                NotificationId = item["NotificationId"].S,
                UserId = item["UserId"].S,
                Type = item["Type"].S,
                Message = item["Message"].S,
                QuestionId = item.ContainsKey("QuestionId") && !string.IsNullOrEmpty(item["QuestionId"].S) ? item["QuestionId"].S : null,
                AnswerId = item.ContainsKey("AnswerId") && !string.IsNullOrEmpty(item["AnswerId"].S) ? item["AnswerId"].S : null,
                CreatedAt = DateTime.Parse(item["CreatedAt"].S),
                IsRead = item.ContainsKey("IsRead") && item["IsRead"].BOOL.HasValue ? item["IsRead"].BOOL.Value : false
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get notifications for user {UserId}", userId);
            return new List<ForumNotification>();
        }
    }

    public async Task MarkNotificationReadAsync(string notificationId, CancellationToken ct)
    {
        await _dynamoDb.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _notificationsTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["NotificationId"] = new AttributeValue { S = notificationId }
            },
            UpdateExpression = "SET IsRead = :val",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":val"] = new AttributeValue { BOOL = true }
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
                    if (!string.IsNullOrEmpty(plan?.Email))
                    {
                        await SendEmailAsync(plan.Email, questionTitle, questionId, ct);
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
                Source = "mamootilsamuel1@gmail.com",
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

    public async Task<int> CleanupInappropriatePostsAsync(CancellationToken ct)
    {
        var deletedCount = 0;
        var questions = await GetAllQuestionsAsync(ct);
        
        foreach (var question in questions)
        {
            var (titleOk, _) = ModerateTextFallback(question.Title);
            var (contentOk, _) = ModerateTextFallback(question.Content);
            
            if (!titleOk || !contentOk)
            {
                await DeleteQuestionAsync(question.QuestionId, ct);
                deletedCount++;
                _logger.LogWarning("Deleted inappropriate question: {QuestionId} - {Title}", question.QuestionId, question.Title);
            }
        }
        
        return deletedCount;
    }
}
