# Azure Deployment Guide

This document explains the deployment process for the SmartTask AI Assistant Backend to Azure App Service.

## Overview

The application is deployed to Azure App Service using GitHub Actions. The workflow supports two environments:
- **Staging**: Deploys when pushing to the `develop` branch
- **Production**: Deploys when pushing to the `main` branch

## Configuration Approach

### Azure App Service Application Settings (Recommended)

The deployment uses **Azure App Service Application Settings** (environment variables) instead of file-based configuration. This approach:

- ✅ Is more secure (secrets are not stored in files)
- ✅ Allows runtime configuration changes without redeployment
- ✅ Follows Azure best practices
- ✅ Prevents accidental secret exposure in logs or artifacts

### How It Works

1. **Environment Variables Override appsettings.json**: ASP.NET Core's configuration system automatically reads environment variables and uses them to override values in `appsettings.json`.

2. **Naming Convention**: Use double underscores (`__`) to represent nested configuration sections:
   - `ConnectionStrings__DefaultConnection` → `ConnectionStrings:DefaultConnection`
   - `JwtSettings__SecretKey` → `JwtSettings:SecretKey`
   - `OpenAi__ApiKey` → `OpenAi:ApiKey`

3. **ASPNETCORE_ENVIRONMENT**: Set to `Production` or `Staging` to control which environment-specific settings are loaded.

## Required GitHub Secrets

### For Production Deployment

Configure these secrets in your GitHub repository settings (`Settings` → `Secrets and variables` → `Actions`):

| Secret Name | Description | Example |
|------------|-------------|---------|
| `AZURE_CREDENTIALS` | Azure Service Principal credentials for authentication | JSON with clientId, clientSecret, subscriptionId, tenantId |
| `AZURE_WEBAPP_PUBLISH_PROFILE_PRODUCTION` | Azure App Service publish profile for production | Downloaded from Azure Portal |
| `DATABASE_CONNECTION_STRING` | Production database connection string | `Server=...;Database=...;User Id=...;Password=...` |
| `JWT_SECRET_KEY` | JWT signing key (min 32 characters) | `YourSuperSecretKeyThatIsAtLeast32CharactersLong123!` |
| `OPENAI_API_KEY` | OpenAI API key for production | `sk-...` |

### For Staging Deployment

| Secret Name | Description |
|------------|-------------|
| `AZURE_CREDENTIALS_STAGING` | Azure Service Principal credentials for staging |
| `AZURE_WEBAPP_PUBLISH_PROFILE_STAGING` | Azure App Service publish profile for staging |
| `STAGING_DATABASE_CONNECTION_STRING` | Staging database connection string |
| `STAGING_JWT_SECRET_KEY` | JWT signing key for staging |
| `STAGING_OPENAI_API_KEY` | OpenAI API key for staging |

### How to Get Azure Credentials

#### Option 1: Create Service Principal (Recommended)

```bash
az ad sp create-for-rbac \
  --name "smarttask-ai-github-actions" \
  --role contributor \
  --scopes /subscriptions/{subscription-id}/resourceGroups/{resource-group} \
  --sdk-auth
```

This will output JSON credentials that you can use as `AZURE_CREDENTIALS`.

#### Option 2: Download Publish Profile

1. Go to Azure Portal
2. Navigate to your App Service
3. Click **Get publish profile** in the Overview section
4. Copy the entire XML content
5. Add it as `AZURE_WEBAPP_PUBLISH_PROFILE_PRODUCTION` secret

## Azure App Service Configuration

### Application Settings in Azure Portal

After deployment, verify these settings are configured in the Azure Portal:

1. Go to your App Service in Azure Portal
2. Navigate to **Configuration** → **Application settings**
3. Verify these settings exist:

```
ConnectionStrings__DefaultConnection = <your-database-connection-string>
JwtSettings__SecretKey = <your-jwt-secret-key>
JwtSettings__Issuer = SmartTask-AI-Production
JwtSettings__Audience = SmartTask-AI-Users
OpenAi__ApiKey = <your-openai-api-key>
ASPNETCORE_ENVIRONMENT = Production
```

### Manual Configuration (Alternative Method)

If you prefer to configure Azure App Service settings manually instead of using the GitHub Action:

1. Go to Azure Portal → Your App Service → Configuration
2. Click **+ New application setting** for each setting
3. Add the settings listed above
4. Click **Save** and then **Continue** to restart the app

## Workflow Features

### Build and Test
- Builds the .NET application
- Creates a deployment artifact
- Runs security analysis

### Deployment Verification
- Validates all required secrets are configured
- Fails early if secrets are missing
- Prevents incomplete deployments

### Configuration Management
- Uses Azure CLI to set App Service application settings
- Secrets are injected directly into Azure (never written to files)
- Configuration is set before deployment to ensure app starts correctly

### Health Checks
- Attempts health checks up to 10 times (production) or 5 times (staging)
- Provides detailed HTTP error code diagnostics
- Includes troubleshooting instructions if checks fail
- Non-blocking (allows manual verification if needed)

### Error Diagnostics
- HTTP 000: Connection failed - app may be starting or crashed
- HTTP 500: Internal Server Error - check application logs for startup exceptions
- HTTP 502: Bad Gateway - app container may not be responding
- HTTP 503: Service Unavailable - app is still starting up

## Troubleshooting

### Deployment Fails with "Missing Secrets"

**Solution**: Configure all required secrets in GitHub repository settings.

1. Go to your GitHub repository
2. Click **Settings** → **Secrets and variables** → **Actions**
3. Click **New repository secret**
4. Add each required secret from the tables above

### App Returns HTTP 500.30 Error

This error indicates the ASP.NET Core app failed to start. Common causes:

1. **Missing Configuration Values**: Check Azure App Service Application Settings
2. **Database Connection Issues**: Verify the connection string is correct
3. **Startup Exceptions**: Check Azure App Service logs

**How to check logs**:
```bash
# View live logs
az webapp log tail --name smarttask-ai-api --resource-group <your-resource-group>

# Download logs
az webapp log download --name smarttask-ai-api --resource-group <your-resource-group>
```

### Health Checks Fail

1. **Check Application Logs**: Use `az webapp log tail` to see real-time logs
2. **Verify Settings**: Ensure all Application Settings are configured in Azure Portal
3. **Check ASPNETCORE_ENVIRONMENT**: Should be set to `Production` or `Staging`
4. **Database Connection**: Verify the database is accessible from Azure
5. **OpenAI API Key**: Ensure the API key is valid and has credits

### Configuration Not Being Applied

If you update Application Settings and they're not reflected:

1. **Restart the App Service**: Changes require a restart
2. **Check Setting Names**: Use double underscores (`__`) not colons (`:`)
3. **Check for Typos**: Setting names are case-sensitive
4. **Verify in Portal**: Confirm settings appear in Azure Portal Configuration

## Local Development

For local development, use `appsettings.Development.json` or `appsettings.json`:

```bash
dotnet run --environment Development
```

The local configuration files are not deployed to Azure and are only used during development.

## Security Best Practices

1. ✅ **Never commit secrets** to the repository
2. ✅ **Use separate secrets** for staging and production
3. ✅ **Rotate secrets regularly** (especially JWT keys and database passwords)
4. ✅ **Use Azure Key Vault** for enhanced secret management (advanced)
5. ✅ **Enable Application Insights** for monitoring and diagnostics
6. ✅ **Restrict App Service** to HTTPS only
7. ✅ **Use managed identities** where possible instead of connection strings

## Support

For issues with deployment:
1. Check the GitHub Actions workflow logs
2. Review Azure App Service logs
3. Verify all secrets are configured correctly
4. Ensure the App Service plan has sufficient resources

## Additional Resources

- [Azure App Service Documentation](https://docs.microsoft.com/azure/app-service/)
- [ASP.NET Core Configuration](https://docs.microsoft.com/aspnet/core/fundamentals/configuration/)
- [GitHub Actions for Azure](https://github.com/Azure/actions)
