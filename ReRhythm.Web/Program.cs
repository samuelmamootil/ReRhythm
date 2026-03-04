using Amazon.BedrockRuntime;        // ✅ Changed from BedrockAgentRuntime
using Amazon.DynamoDBv2;
using Amazon.S3;
using Amazon.Textract;
using Amazon.Rekognition;
using Amazon.Comprehend;
using Amazon.SimpleEmail;
using ReRhythm.Core.Services;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ── AWS Core ──────────────────────────────────────────
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());

// ── AWS Services ──────────────────────────────────────
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddAWSService<IAmazonTextract>();
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddAWSService<IAmazonBedrockRuntime>();  // ✅ Now resolves correctly
builder.Services.AddAWSService<IAmazonRekognition>();
builder.Services.AddAWSService<IAmazonComprehend>();
builder.Services.AddAWSService<IAmazonSimpleEmailService>();

// ── App Services ──────────────────────────────────────
builder.Services.AddScoped<TextractService>();
builder.Services.AddScoped<BedrockRAGService>();
builder.Services.AddScoped<RoadmapService>();
builder.Services.AddScoped<DynamoDbService>();
builder.Services.AddScoped<ResumeGeneratorService>();
builder.Services.AddScoped<CertificateService>();
builder.Services.AddScoped<BadgeService>();
builder.Services.AddScoped<ForumService>();
builder.Services.AddScoped<NetworkingService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<ResumeBuilderService>(sp => 
    new ResumeBuilderService(
        sp.GetRequiredService<IAmazonBedrockRuntime>(),
        sp.GetRequiredService<IConfiguration>()["ReRhythm:BedrockModelId"] ?? "us.anthropic.claude-sonnet-4-20250514-v1:0"
    )
);

// ── MVC ───────────────────────────────────────────────
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Resume}/{action=Upload}/{id?}");

app.Run();
