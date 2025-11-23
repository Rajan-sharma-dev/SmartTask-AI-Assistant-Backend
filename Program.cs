// C#
using MiddleWareWebApi;
using MiddleWareWebApi.data;
using MiddleWareWebApi.MiddleWare;
using MiddleWareWebApi.Services;
using MiddleWareWebApi.Services.Interfaces;
using MiddleWareWebApi.Models.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add HttpContextAccessor for accessing HttpContext in services
builder.Services.AddHttpContextAccessor();

// Configure JWT Settings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();

// Database Context
builder.Services.AddSingleton<DapperContext>();

// Services - Principal is automatically available through ICurrentUserService
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

// ?? Configure Cookie Policy for automatic token management
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => false; // Disable consent for essential cookies
    options.MinimumSameSitePolicy = SameSiteMode.Strict;
    options.Secure = CookieSecurePolicy.Always; // Set to SameAsRequest for development HTTP
});

// JWT Authentication (supports both cookies and headers)
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // Set to true in production
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
                "https://localhost:8080"
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // 🔐 Required for cookies
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Enable CORS
app.UseCors("AllowMultipleFrameworks");

// 📁 Enable static files (for database-management.html and other static content)
app.UseStaticFiles();

// ?? Use Cookie Policy (for automatic token management)
app.UseCookiePolicy();

// Add middleware in correct order
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<LoggingMiddleware>();

// Authentication & Authorization - This sets HttpContext.User (Principal)
app.UseAuthentication(); // This MUST come before custom JWT middleware
app.UseAuthorization();

// Custom middleware - Principal is now available in HttpContext.User
app.UseMiddleware<JwtAuthenticationMiddleware>(); // This enhances the Principal with additional context
app.UseMiddleware<TransformMiddleware>();
app.UseMiddleware<DynamicServiceMiddleWare>();
app.UseMiddleware<ResponseMiddleware>();

app.MapControllers();

app.Run();
