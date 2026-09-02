using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Conversations;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Normalization;
using AI.DocumentAssistant.Server.Processing;
using AI.DocumentAssistant.Server.Retrieval;
using AI.DocumentAssistant.Server.Rag;
using AI.DocumentAssistant.Server.Storage;
using AI.DocumentAssistant.Server.Understanding;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is not configured. Configure it with User Secrets for local development.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsqlOptions => npgsqlOptions.UseVector()));

builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
builder.Services.AddSingleton<IDocumentProcessingQueue, DocumentProcessingQueue>();
builder.Services.AddSingleton<IDocumentTextExtractor, PdfDocumentTextExtractor>();
builder.Services.AddSingleton<IDocumentTextExtractor, DocxDocumentTextExtractor>();
builder.Services.AddSingleton<IDocumentTokenizer, Cl100kDocumentTokenizer>();
builder.Services.AddSingleton<IDocumentChunkGenerator, DocumentChunkGenerator>();
builder.Services.AddSingleton<IDocumentTextNormalizer, DocumentTextNormalizer>();
builder.Services.AddSingleton<IDocumentUnderstandingInputBuilder, DocumentUnderstandingInputBuilder>();
builder.Services.AddSingleton<DocumentUnderstandingValidator>();
builder.Services.AddSingleton<IOpenAIEmbeddingClient, OpenAISdkEmbeddingClient>();
builder.Services.AddSingleton<ITextEmbeddingService, OpenAITextEmbeddingService>();
builder.Services.AddSingleton<IDocumentUnderstandingClient, OpenAIDocumentUnderstandingClient>();
builder.Services.AddSingleton<IOpenAIAnswerClient, OpenAIResponsesAnswerClient>();
builder.Services.AddSingleton<IGroundedAnswerService, OpenAIGroundedAnswerService>();
builder.Services.AddSingleton<IRagContextBuilder, RagContextBuilder>();
builder.Services.AddSingleton<IConversationHistoryContextBuilder, ConversationHistoryContextBuilder>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IDocumentChunkingService, DocumentChunkingService>();
builder.Services.AddScoped<IDocumentNormalizationService, DocumentNormalizationService>();
builder.Services.AddScoped<IDocumentEmbeddingService, DocumentEmbeddingService>();
builder.Services.AddScoped<IDocumentUnderstandingService, DocumentUnderstandingService>();
builder.Services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();
builder.Services.AddScoped<ISemanticChunkSearch, PgvectorSemanticChunkSearch>();
builder.Services.AddScoped<ISemanticRetrievalService, SemanticRetrievalService>();
builder.Services.AddScoped<IProjectQuestionAnsweringService, ProjectQuestionAnsweringService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddHostedService<DocumentProcessingWorker>();

builder.Services.AddOptions<DocumentChunkingOptions>()
    .Bind(builder.Configuration.GetSection(DocumentChunkingOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => options.MinimumTokens <= options.TargetTokens,
        "Chunking MinimumTokens must not exceed TargetTokens.")
    .Validate(
        options => options.TargetTokens <= options.MaximumTokens,
        "Chunking TargetTokens must not exceed MaximumTokens.")
    .Validate(
        options => options.OverlapTokens <= options.MinimumTokens,
        "Chunking OverlapTokens must not exceed MinimumTokens.")
    .ValidateOnStart();

builder.Services.AddOptions<DocumentNormalizationOptions>()
    .Bind(builder.Configuration.GetSection(DocumentNormalizationOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => options.MinimumPageCountForBoilerplateDetection >= 3,
        "DocumentNormalization MinimumPageCountForBoilerplateDetection must be at least 3.")
    .Validate(
        options => options.MinimumCandidateBlockLength <= options.MinimumLocalCandidateBlockLength,
        "DocumentNormalization MinimumCandidateBlockLength must not exceed MinimumLocalCandidateBlockLength.")
    .Validate(
        options => options.MinimumLocalCandidateBlockLength <= options.MaximumCandidateLength,
        "DocumentNormalization MinimumLocalCandidateBlockLength must not exceed MaximumCandidateLength.")
    .ValidateOnStart();

builder.Services.AddOptions<OpenAIEmbeddingOptions>()
    .Bind(builder.Configuration.GetSection(OpenAIEmbeddingOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.EmbeddingModel),
        "OpenAI EmbeddingModel must not be empty.")
    .Validate(
        options => options.EmbeddingDimensions == EmbeddingArchitecture.Dimensions,
        $"OpenAI EmbeddingDimensions must be {EmbeddingArchitecture.Dimensions}; changing it requires an EF migration.")
    .ValidateOnStart();

builder.Services.AddOptions<OpenAIAnswerOptions>()
    .Bind(builder.Configuration.GetSection(OpenAIAnswerOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.AnswerModel),
        "OpenAI AnswerModel must not be empty.")
    .ValidateOnStart();

builder.Services.AddOptions<OpenAIDocumentUnderstandingOptions>()
    .Bind(builder.Configuration.GetSection(OpenAIDocumentUnderstandingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 8;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "AI.DocumentAssistant.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    options.Events = new CookieAuthenticationEvents
    {
        OnRedirectToLogin = async context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(
                new ApiErrorResponse("Authentication is required."),
                context.HttpContext.RequestAborted);
        },
        OnRedirectToAccessDenied = async context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new ApiErrorResponse("You do not have permission to perform this action."),
                context.HttpContext.RequestAborted);
        }
    };
});

builder.Services.AddControllers(options =>
    {
        options.Filters.Add(new ProducesAttribute("application/json"));
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => string.IsNullOrEmpty(entry.Key) ? "request" : ToCamelCase(entry.Key),
                    entry => entry.Value!.Errors
                        .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "The supplied value is invalid."
                            : error.ErrorMessage)
                        .ToArray());

            return new BadRequestObjectResult(
                new ApiErrorResponse("Validation failed.", errors));
        };
    });

var app = builder.Build();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GlobalExceptionHandler");

        logger.LogError(exception, "An unhandled exception occurred while processing the request.");

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            new ApiErrorResponse("An unexpected error occurred."),
            context.RequestAborted);
    });
});

app.UseDefaultFiles();
app.MapStaticAssets();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "AI Document Assistant"
}))
    .AllowAnonymous();

app.MapFallbackToFile("/index.html");

app.Run();

static string ToCamelCase(string value) =>
    string.IsNullOrEmpty(value)
        ? value
        : char.ToLowerInvariant(value[0]) + value[1..];
