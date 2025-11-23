namespace MiddleWareWebApi.MiddleWare
{
    /// <summary>
    /// Centralized configuration for public services that don't require authentication
    /// </summary>
    public static class PublicServicesConfig
    {
        /// <summary>
        /// Services that can be called without authentication
        /// </summary>
        public static readonly HashSet<string> PublicServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 🔓 Authentication services (available without login)
            "IdentityService.LoginAsync",
            "IdentityService.RegisterAsync", 
            "IdentityService.RefreshTokenAsync",
            "IdentityService.ForgotPasswordAsync",
            "IdentityService.ResetPasswordAsync",
            "IdentityService.VerifyEmailAsync",
            "IdentityService.ResendVerificationAsync",
            
            // 🔓 Public user services (for registration validation)
            "UserService.GetUserByUsernameAsync",   // For login validation
            "UserService.GetUserByEmailAsync",      // For login validation  
            "UserService.CheckUsernameExistsAsync", // For registration validation
            "UserService.CheckEmailExistsAsync",    // For registration validation
            "UserService.CreateUserAsync",          // For registration
            "UserService.UpdatePasswordAsync",      // For password reset
            
            // 🔓 Health check services
            "HealthService.GetHealthStatusAsync",
            "SystemService.GetPublicInfoAsync",
            "SystemService.GetVersionAsync",
            
            // 🔓 Public information services
            "InfoService.GetPublicSettingsAsync",
            "InfoService.GetTermsOfServiceAsync",
            "InfoService.GetPrivacyPolicyAsync"
        };

        /// <summary>
        /// Controller endpoints that should be public (no authentication required)
        /// </summary>
        public static readonly HashSet<string> PublicControllerEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Auth controller endpoints
            "/api/auth/login",
            "/api/auth/register", 
            "/api/auth/refresh-token",
            "/api/auth/forgot-password",
            "/api/auth/reset-password",
            "/api/auth/verify-email",
            "/api/auth/resend-verification",
            
            // Health check endpoints
            "/api/health",
            "/api/health/status",
            
            // Public information endpoints
            "/api/info/version",
            "/api/info/terms",
            "/api/info/privacy"
        };

        /// <summary>
        /// Check if a service method is public (no authentication required)
        /// </summary>
        /// <param name="serviceName">Service name (e.g., "IdentityService")</param>
        /// <param name="methodName">Method name (e.g., "LoginAsync")</param>
        /// <returns>True if the service is public, false if authentication is required</returns>
        public static bool IsPublicService(string serviceName, string methodName)
        {
            var serviceMethodKey = $"{serviceName}.{methodName}";
            return PublicServices.Contains(serviceMethodKey);
        }

        /// <summary>
        /// Check if a controller endpoint is public (no authentication required)
        /// </summary>
        /// <param name="path">Request path (e.g., "/api/auth/login")</param>
        /// <returns>True if the endpoint is public, false if authentication is required</returns>
        public static bool IsPublicEndpoint(string path)
        {
            return PublicControllerEndpoints.Contains(path);
        }

        /// <summary>
        /// Check if the current HTTP request is calling a public service
        /// </summary>
        /// <param name="context">HTTP context</param>
        /// <returns>True if calling a public service, false if calling a protected service</returns>
        public static bool IsPublicServiceCall(HttpContext context)
        {
            var segments = context.Request.Path.Value?.Split('/');
            
            // Check if this is a dynamic service call: /api/services/{ServiceName}/{MethodName}
            if (segments != null && segments.Length >= 5 && 
                segments[1].Equals("api", StringComparison.OrdinalIgnoreCase) &&
                segments[2].Equals("services", StringComparison.OrdinalIgnoreCase))
            {
                var serviceName = segments[3];
                var method = segments[4];
                return IsPublicService(serviceName, method);
            }

            // Check if this is a public controller endpoint
            var path = context.Request.Path.Value?.ToLowerInvariant();
            if (!string.IsNullOrEmpty(path))
            {
                return IsPublicEndpoint(path);
            }

            return false;
        }

        /// <summary>
        /// Get user-friendly description of service access level
        /// </summary>
        /// <param name="serviceName">Service name</param>
        /// <param name="methodName">Method name</param>
        /// <returns>Description of access level</returns>
        public static string GetServiceAccessDescription(string serviceName, string methodName)
        {
            if (IsPublicService(serviceName, methodName))
            {
                return "🔓 Public Service (No authentication required)";
            }
            else
            {
                return "🔒 Protected Service (Authentication required)";
            }
        }

        /// <summary>
        /// Add a new public service at runtime (for extensibility)
        /// </summary>
        /// <param name="serviceName">Service name</param>
        /// <param name="methodName">Method name</param>
        public static void AddPublicService(string serviceName, string methodName)
        {
            var serviceMethodKey = $"{serviceName}.{methodName}";
            PublicServices.Add(serviceMethodKey);
        }

        /// <summary>
        /// Remove a public service (make it protected)
        /// </summary>
        /// <param name="serviceName">Service name</param>
        /// <param name="methodName">Method name</param>
        public static void RemovePublicService(string serviceName, string methodName)
        {
            var serviceMethodKey = $"{serviceName}.{methodName}";
            PublicServices.Remove(serviceMethodKey);
        }
    }
}