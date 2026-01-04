// C#
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MiddleWareWebApi;
using MiddleWareWebApi.data;
using MiddleWareWebApi.Extensions;
using MiddleWareWebApi.MiddleWare;
using MiddleWareWebApi.Models.Configuration;
using MiddleWareWebApi.Services;
using MiddleWareWebApi.Services.Interfaces;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add environment variables to configuration
builder.Configuration.AddEnvironmentVariables();

// Add services to the container
builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add HttpContextAccessor for accessing HttpContext in services
builder.Services.AddHttpContextAccessor();

// Configure JWT Settings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();

// Validate JWT Settings - Add logging to help diagnose startup issues
if (jwtSettings == null || string.IsNullOrEmpty(jwtSettings.SecretKey))
{
    Console.WriteLine("❌ ERROR: JWT Settings not found or SecretKey is empty");
    Console.WriteLine($"Environment: {builder.Environment.EnvironmentName}");
    Console.WriteLine("Available configuration keys:");
    foreach (var config in builder.Configuration.AsEnumerable())
    {
        Console.WriteLine($"  {config.Key}: {(config.Key.ToLower().Contains("secret") || config.Key.ToLower().Contains("key") ? "[HIDDEN]" : config.Value)}");
    }
}
else
{
    Console.WriteLine($"✅ JWT Settings loaded successfully for environment: {builder.Environment.EnvironmentName}");
}

// Database Context
builder.Services.AddSingleton<DapperContext>();

// Validate Database Connection String
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    Console.WriteLine("❌ ERROR: Database connection string 'DefaultConnection' not found");
}
else
{
    Console.WriteLine("✅ Database connection string loaded successfully");
    Console.WriteLine($"   Server: {(connectionString.Contains("localhost") ? "localhost (development)" : "Azure SQL (production)")}");
}

// Configure OpenAI Settings
builder.Services.Configure<OpenAiSettings>(builder.Configuration.GetSection("OpenAi"));
var openAiSettings = builder.Configuration.GetSection("OpenAi").Get<OpenAiSettings>();

// Validate OpenAI Settings
if (openAiSettings == null || string.IsNullOrEmpty(openAiSettings.ApiKey))
{
    Console.WriteLine("❌ ERROR: OpenAI Settings not found or ApiKey is empty");
}
else
{
    Console.WriteLine("✅ OpenAI Settings loaded successfully");
}

// Add Prompt Management Services
builder.Services.AddPromptManagement(builder.Configuration);

// Add HttpClient for OpenAI Services
builder.Services.AddHttpClient<IOpenAiService, OpenAiService>();
builder.Services.AddHttpClient<IOpenAiProjectService, OpenAiProjectService>();

// Services - Principal is automatically available through ICurrentUserService
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IOpenAiService, OpenAiService>();
builder.Services.AddScoped<IOpenAiProjectService, OpenAiProjectService>();

// Add AI Command Services (with new prompt management architecture)
builder.Services.AddAiCommandServices();

// ?? Configure Cookie Policy for automatic token management
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => false; // Disable consent for essential cookies
    options.MinimumSameSitePolicy = SameSiteMode.Strict;
    options.Secure = CookieSecurePolicy.Always; // Set to SameAsRequest for development HTTP
});

// JWT Authentication (supports both cookies and headers)
try
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = true; // Always require HTTPS in production
        options.SaveToken = true;
        
        // ?? Configure to read JWT from both cookies and headers
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Try to get token from cookie first (automatic)
                var token = context.Request.Cookies["accessToken"];
                if (!string.IsNullOrEmpty(token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                var result = System.Text.Json.JsonSerializer.Serialize(new { message = "Authentication failed" });
                return context.Response.WriteAsync(result);
            },
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                var result = System.Text.Json.JsonSerializer.Serialize(new { message = "You are not authorized" });
                return context.Response.WriteAsync(result);
            }
        };

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings?.Issuer,
            ValidAudience = jwtSettings?.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings?.SecretKey ?? "")),
            ClockSkew = TimeSpan.Zero
        };
    });
    
    Console.WriteLine("✅ JWT Authentication configured successfully");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ ERROR configuring JWT Authentication: {ex.Message}");
    throw; // Re-throw to prevent app from starting with broken auth
}

// Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("UserOrAdmin", policy => policy.RequireRole("User", "Admin"));
});

// CORS (configured for multiple frameworks)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMultipleFrameworks", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",  // ← React App
                "http://localhost:8080",  // ← Vue App
                "https://localhost:3000", // ← HTTPS versions
                "https://localhost:8080",
                "http://localhost:5174"
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // 🔐 Required for cookies
    });
});

var app = builder.Build();

app.Map("/health", healthApp =>
{
    healthApp.Run(async context =>
    {
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                environment = app.Environment.EnvironmentName
            })
        );
    });
});

app.Map("/settings", setting =>
{
    setting.Run(async context =>
    {
        await context.Response.WriteAsync(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                jwtSettings = jwtSettings,
                openAiSettings = openAiSettings,
                connectionString = connectionString
            })
        );
    });
});

app.Map("/hello", helloApp =>
{
    helloApp.Run(async context =>
    {
        await context.Response.WriteAsync("Hello World");
    });
});

app.Map("/SmartTask-AI", helloApp =>
{
    helloApp.Run(async context =>
    {
        await context.Response.WriteAsync("App is running fine");
    });
});

app.Map("/test-db", value =>
{
    value.Run(async context =>
    {
        context.Response.ContentType = "application/json";
        
        var db = context.RequestServices.GetRequiredService<DapperContext>();
        try
        {
            using var connection = db.CreateConnection();
            
            // Cast to SqlConnection for async methods and additional properties
            if (connection is SqlConnection sqlConnection)
            {
                // Actually test the database connection
                var startTime = DateTime.UtcNow;
                await sqlConnection.OpenAsync();
                
                // Execute a simple query to verify connectivity using Dapper
                var result = await sqlConnection.QueryFirstOrDefaultAsync<int>("SELECT 1");
                
                var responseTime = DateTime.UtcNow - startTime;
                
                await context.Response.WriteAsync(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        status = "Success",
                        connectionState = sqlConnection.State.ToString(),
                        testQuery = "SELECT 1",
                        testResult = result.ToString(),
                        responseTime = $"{responseTime.TotalMilliseconds:F2}ms",
                        server = sqlConnection.DataSource,
                        database = sqlConnection.Database,
                        timestamp = DateTime.UtcNow
                    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
                );
            }
            else
            {
                // Fallback for non-SQL Server connections
                connection.Open();
                await context.Response.WriteAsync(
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        status = "Success",
                        connectionState = connection.State.ToString(),
                        testQuery = "Connection opened successfully",
                        connectionType = connection.GetType().Name,
                        timestamp = DateTime.UtcNow
                    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
                );
            }
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    status = "Error",
                    errorType = ex.GetType().Name,
                    message = ex.Message,
                    timestamp = DateTime.UtcNow
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
            );
        }
    });
});

Console.WriteLine($"🚀 Starting SmartTask AI Assistant API - Environment: {app.Environment.EnvironmentName}");
Console.WriteLine($"   Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Enable CORS
app.UseCors("AllowMultipleFrameworks");

// Configure Prompt Management
app.UsePromptManagement(app.Configuration);

// 📁 Enable static files (for database-management.html and other static content)
app.UseStaticFiles();


app.UseCookiePolicy();

// Add middleware in correct order
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<LoggingMiddleware>();

// Authentication & Authorization - This sets HttpContext.User (Principal)
app.UseAuthentication(); // This MUST come before custom JWT middleware
app.UseAuthorization();

app.MapWhen(context => context.Request.Path.StartsWithSegments("/api/public"), appBuilder =>
{
    appBuilder.UseRouting();
    appBuilder.UseEndpoints(endpoints => endpoints.MapControllers());
});
// Custom middleware - Principal is now available in HttpContext.User
app.UseMiddleware<JwtAuthenticationMiddleware>(); // This enhances the Principal with additional context
app.UseMiddleware<TransformMiddleware>();
app.UseMiddleware<DynamicServiceMiddleWare>();
app.UseMiddleware<ResponseMiddleware>();

app.MapControllers();

try
{
    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"❌ FATAL ERROR: Application failed to start: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    throw;
}
