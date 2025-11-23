using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Azure;
using Newtonsoft.Json.Linq;
using System.Security.Claims;

namespace MiddleWareWebApi.MiddleWare
{
    public class DynamicServiceMiddleWare
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<DynamicServiceMiddleWare> _logger;

        public DynamicServiceMiddleWare(RequestDelegate next, ILogger<DynamicServiceMiddleWare> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, IServiceProvider serviceProvider)
        {
            var segments = context.Request.Path.Value?.Split('/');
            var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var jsonBody = await reader.ReadToEndAsync();
            
            // 🔧 FIXED: Correct path parsing for /api/services/{ServiceName}/{MethodName}
            // Expected URL: /api/services/IdentityService/RegisterAsync
            // segments[0] = "" (empty)
            // segments[1] = "api" 
            // segments[2] = "services"
            // segments[3] = "IdentityService"   ← Service Name
            // segments[4] = "RegisterAsync"     ← Method Name
            
            if (segments != null && segments.Length >= 5 && 
                segments[1].Equals("api", StringComparison.OrdinalIgnoreCase) &&
                segments[2].Equals("services", StringComparison.OrdinalIgnoreCase))
            {
                var serviceName = segments[3];
                var method = segments[4];
                var serviceMethodKey = $"{serviceName}.{method}";

                _logger.LogDebug("Processing dynamic service call: {ServiceMethodKey} from path: {Path}", 
                    serviceMethodKey, context.Request.Path);

                // 🔓🔒 Check if this is a public service using centralized config
                var isPublicService = PublicServicesConfig.IsPublicService(serviceName, method);
                
                // 🔒 Check authentication for protected services
                if (!isPublicService)
                {
                    var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;
                    
                    if (!isAuthenticated)
                    {
                        _logger.LogWarning("Unauthenticated access attempt to protected service: {ServiceName}.{Method}", 
                            serviceName, method);
                        
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = "Authentication required",
                            message = "You must be logged in to access this service",
                            service = serviceMethodKey,
                            accessLevel = PublicServicesConfig.GetServiceAccessDescription(serviceName, method)
                        });
                        return;
                    }
                }

                // Log the service call with appropriate user info
                var userId = isPublicService 
                    ? "Anonymous" 
                    : context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "Unknown";

                _logger.LogInformation("Calling service: {ServiceName}.{Method} for user: {UserId} ({AccessLevel})", 
                    serviceName, method, userId, PublicServicesConfig.GetServiceAccessDescription(serviceName, method));

                // 🔧 FIXED: Try to resolve service from DI container (handle both interface and concrete registrations)
                object? service = null;
                Type? serviceType = null;

                // Try to get service by interface first (e.g., IIdentityService)
                var interfaceTypeName = $"MiddleWareWebApi.Services.Interfaces.I{serviceName}";
                var interfaceType = Type.GetType(interfaceTypeName);
                if (interfaceType != null)
                {
                    service = serviceProvider.GetService(interfaceType);
                    if (service != null)
                    {
                        serviceType = interfaceType;
                        _logger.LogDebug("Found service by interface: {InterfaceType}", interfaceTypeName);
                    }
                }

                // If not found by interface, try concrete class (e.g., UserService, TaskService)
                if (service == null)
                {
                    var concreteTypeName = $"MiddleWareWebApi.Services.{serviceName}";
                    var concreteType = Type.GetType(concreteTypeName);
                    if (concreteType != null)
                    {
                        service = serviceProvider.GetService(concreteType);
                        if (service != null)
                        {
                            serviceType = concreteType;
                            _logger.LogDebug("Found service by concrete type: {ConcreteType}", concreteTypeName);
                        }
                    }
                }

                if (service != null && serviceType != null)
                {
                    var methodInfo = service.GetType().GetMethod(method);
                    if (methodInfo == null)
                    {
                        _logger.LogWarning("Method not found: {ServiceName}.{Method}", serviceName, method);
                        context.Response.StatusCode = 404;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = $"Method '{method}' not found in service '{serviceName}'",
                            service = serviceMethodKey,
                            availableMethods = service.GetType().GetMethods()
                                .Where(m => m.IsPublic && !m.IsSpecialName)
                                .Select(m => m.Name)
                                .Distinct()
                                .ToArray()
                        });
                        return;
                    }

                    var parameters = methodInfo.GetParameters();
                    var args = new object?[parameters.Length];

                    // Process method parameters from request body
                    if (parameters.Length > 0)
                    {
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            var param = parameters[i];
                            object? nameValue = null;

                            // Skip parameters that are dependency injected (like ICurrentUserService)
                            if (IsServiceParameter(param.ParameterType))
                            {
                                continue; // Let DI handle this parameter
                            }

                            // 🌐 Special handling for IP address parameter in IdentityService methods
                            if (param.Name.Equals("ipAddress", StringComparison.OrdinalIgnoreCase) && 
                                param.ParameterType == typeof(string))
                            {
                                nameValue = GetIpAddress(context);
                                _logger.LogDebug("Auto-injected IP address: {IpAddress} for {ServiceMethod}", 
                                    nameValue, serviceMethodKey);
                            }
                            else if (!string.IsNullOrEmpty(jsonBody) && jsonBody != "{}")
                            {
                                var jsonObject = JObject.Parse(jsonBody);
                                
                                if (!jsonObject.TryGetValue(param.Name, StringComparison.OrdinalIgnoreCase, out var token))
                                {
                                    if (!param.HasDefaultValue)
                                    {
                                        _logger.LogWarning("Missing required parameter: {ParameterName} for {ServiceMethod}", 
                                            param.Name, serviceMethodKey);
                                        context.Response.StatusCode = 400;
                                        await context.Response.WriteAsJsonAsync(new
                                        {
                                            error = $"Missing required parameter '{param.Name}' in request body",
                                            service = serviceMethodKey,
                                            accessLevel = PublicServicesConfig.GetServiceAccessDescription(serviceName, method),
                                            expectedParameters = parameters.Where(p => !IsServiceParameter(p.ParameterType))
                                                .Select(p => new { name = p.Name, type = p.ParameterType.Name, required = !p.HasDefaultValue })
                                        });
                                        return;
                                    }
                                    nameValue = param.DefaultValue;
                                }
                                else
                                {
                                    try
                                    {
                                        if (param.ParameterType.IsClass && param.ParameterType != typeof(string))
                                        {
                                            // Deserialize complex object/DTO
                                            nameValue = token.ToObject(param.ParameterType);

                                            // Validate DTO fields
                                            if (nameValue != null)
                                            {
                                                var validationContext = new ValidationContext(nameValue, null, null);
                                                var results = new List<ValidationResult>();
                                                bool isValid = Validator.TryValidateObject(nameValue, validationContext, results, true);

                                                if (!isValid)
                                                {
                                                    _logger.LogWarning("Validation failed for parameter: {ParameterName} in {ServiceMethod}", 
                                                        param.Name, serviceMethodKey);
                                                    context.Response.StatusCode = 400;
                                                    await context.Response.WriteAsJsonAsync(new
                                                    {
                                                        error = $"Validation failed for parameter '{param.Name}'",
                                                        details = results.Select(r => r.ErrorMessage),
                                                        service = serviceMethodKey,
                                                        accessLevel = PublicServicesConfig.GetServiceAccessDescription(serviceName, method)
                                                    });
                                                    return;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Deserialize primitive type
                                            nameValue = token.ToObject(param.ParameterType);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error deserializing parameter: {ParameterName} for {ServiceMethod}", 
                                            param.Name, serviceMethodKey);
                                        context.Response.StatusCode = 400;
                                        await context.Response.WriteAsJsonAsync(new
                                        {
                                            error = $"Invalid value for parameter '{param.Name}'",
                                            details = ex.Message,
                                            service = serviceMethodKey
                                        });
                                        return;
                                    }
                                }
                            }
                            else if (!param.HasDefaultValue)
                            {
                                _logger.LogWarning("No request body provided for required parameter: {ParameterName} in {ServiceMethod}", 
                                    param.Name, serviceMethodKey);
                                context.Response.StatusCode = 400;
                                await context.Response.WriteAsJsonAsync(new
                                {
                                    error = $"Missing required parameter '{param.Name}'",
                                    message = "Request body is required for this service method",
                                    service = serviceMethodKey,
                                    accessLevel = PublicServicesConfig.GetServiceAccessDescription(serviceName, method)
                                });
                                return;
                            }

                            args[i] = nameValue;
                        }
                    }

                    try
                    {
                        // 🔓🔒 Call service method (public or protected)
                        _logger.LogDebug("Invoking {ServiceName}.{Method} with {ArgCount} arguments", 
                            serviceName, method, args.Count(a => a != null));

                        var result = methodInfo.Invoke(service, args);
                        
                        context.Items["ResponseData"] = result;

                        _logger.LogInformation("Service call completed successfully: {ServiceName}.{Method} for user: {UserId} ({AccessLevel})", 
                            serviceName, method, userId, PublicServicesConfig.GetServiceAccessDescription(serviceName, method));
                    }
                    catch (System.Reflection.TargetInvocationException tex) when (tex.InnerException != null)
                    {
                        var innerException = tex.InnerException;
                        
                        _logger.LogError(innerException, "Service method threw exception: {ServiceName}.{Method} ({AccessLevel})", 
                            serviceName, method, PublicServicesConfig.GetServiceAccessDescription(serviceName, method));

                        if (innerException is UnauthorizedAccessException)
                        {
                            context.Response.StatusCode = 403;
                            await context.Response.WriteAsJsonAsync(new
                            {
                                error = "Access denied",
                                message = innerException.Message,
                                service = serviceMethodKey,
                                accessLevel = PublicServicesConfig.GetServiceAccessDescription(serviceName, method)
                            });
                            return;
                        }

                        if (innerException is ArgumentException argEx)
                        {
                            context.Response.StatusCode = 400;
                            await context.Response.WriteAsJsonAsync(new
                            {
                                error = "Invalid arguments",
                                message = argEx.Message,
                                service = serviceMethodKey
                            });
                            return;
                        }
                        
                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = "An error occurred while processing your request",
                            message = innerException.Message,
                            service = serviceMethodKey,
                            accessLevel = PublicServicesConfig.GetServiceAccessDescription(serviceName, method)
                        });
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error invoking service method: {ServiceName}.{Method} ({AccessLevel})", 
                            serviceName, method, PublicServicesConfig.GetServiceAccessDescription(serviceName, method));
                        
                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = "An error occurred while processing your request",
                            details = ex.Message,
                            service = serviceMethodKey,
                            accessLevel = PublicServicesConfig.GetServiceAccessDescription(serviceName, method)
                        });
                        return;
                    }
                }
                else
                {
                    _logger.LogWarning("Service not found in DI container: {ServiceName}. Tried interface: I{ServiceName}, Concrete: {ServiceName}", 
                        serviceName, serviceName, serviceName);
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = $"Service '{serviceName}' not found in dependency injection container",
                        details = "Make sure the service is registered in Program.cs",
                        triedTypes = new[]
                        {
                            $"MiddleWareWebApi.Services.Interfaces.I{serviceName}",
                            $"MiddleWareWebApi.Services.{serviceName}"
                        }
                    });
                    return;
                }
            }

            await _next(context);
        }

        // Helper method to identify service parameters that should be dependency injected
        private static bool IsServiceParameter(Type parameterType)
        {
            return (parameterType.Name.StartsWith("I") && parameterType.IsInterface) ||
                   parameterType.Name.EndsWith("Service") ||
                   parameterType == typeof(IServiceProvider) ||
                   parameterType == typeof(ILogger<>) ||
                   (parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(ILogger<>));
        }

        // 🌐 Extract IP address from HTTP context (same logic as AuthController)
        private static string GetIpAddress(HttpContext context)
        {
            // Check for forwarded IP (behind proxy/load balancer)
            if (context.Request.Headers.ContainsKey("X-Forwarded-For"))
            {
                var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
                return forwardedFor.Split(',')[0].Trim();
            }

            // Check for real IP (behind proxy)
            if (context.Request.Headers.ContainsKey("X-Real-IP"))
            {
                return context.Request.Headers["X-Real-IP"].ToString();
            }

            // Direct connection IP
            var remoteIp = context.Connection.RemoteIpAddress?.ToString();
            
            // Handle localhost scenarios
            if (remoteIp == "::1" || remoteIp == "127.0.0.1")
            {
                return "127.0.0.1"; // Normalize localhost
            }

            return remoteIp ?? "unknown";
        }
    }

    // Keep UserContext for backward compatibility
    public class UserContext
    {
        public string? UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public bool IsAuthenticated { get; set; }
        public Dictionary<string, string> Claims { get; set; } = new();

        public bool IsInRole(string role) => Role.Equals(role, StringComparison.OrdinalIgnoreCase);
        public bool HasClaim(string claimType) => Claims.ContainsKey(claimType);
        public string? GetClaimValue(string claimType) => Claims.TryGetValue(claimType, out var value) ? value : null;
    }
}
