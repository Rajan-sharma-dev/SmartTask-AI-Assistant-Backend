namespace MiddleWareWebApi.MiddleWare
{
    public class RoleAuthorizationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _requiredRole;
        private readonly ILogger<RoleAuthorizationMiddleware> _logger;

        public RoleAuthorizationMiddleware(RequestDelegate next, string requiredRole, ILogger<RoleAuthorizationMiddleware>? logger = null)
        {
            _next = next;
            _requiredRole = requiredRole;
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RoleAuthorizationMiddleware>.Instance;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 🔓 Check if this is a public service call using centralized config
            if (PublicServicesConfig.IsPublicServiceCall(context))
            {
                _logger.LogDebug("Skipping role authorization for public service: {Path}", context.Request.Path);
                await _next(context);
                return;
            }

            // 🔒 For protected services, enforce role authorization
            if (!context.User.Identity.IsAuthenticated)
            {
                _logger.LogWarning("Unauthenticated user attempted to access role-protected resource: {Path}", context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Authentication required");
                return;
            }

            if (!context.User.IsInRole(_requiredRole))
            {
                _logger.LogWarning("User {UserId} with roles [{Roles}] attempted to access resource requiring role '{RequiredRole}': {Path}", 
                    context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                    string.Join(", ", context.User.Claims.Where(c => c.Type == System.Security.Claims.ClaimTypes.Role).Select(c => c.Value)),
                    _requiredRole,
                    context.Request.Path);
                    
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync($"Role '{_requiredRole}' required");
                return;
            }

            _logger.LogDebug("Role authorization passed for user with role '{RequiredRole}': {Path}", _requiredRole, context.Request.Path);
            await _next(context);
        }
    }
}
