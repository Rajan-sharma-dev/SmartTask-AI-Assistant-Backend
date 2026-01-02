# GitHub Actions Workflow Changes

## Summary
Fixed critical issues in the Azure deployment workflow that were causing HTTP 500.30 errors and deployment failures.

## Changes Made

### 1. Replaced File-Based Configuration with Azure App Service Settings

**Problem**: The workflow created `appsettings.Production.json` using heredoc with single quotes (`'EOF'`), which prevented GitHub Actions variable interpolation. Secrets like `${{ secrets.OPENAI_API_KEY }}` were written literally instead of being substituted.

**Solution**: Removed file-based configuration and replaced it with Azure App Service Application Settings using the `azure/appservice-settings@v1` action. This:
- Properly injects secrets as environment variables
- Follows Azure best practices for configuration management
- Allows runtime configuration changes
- Is more secure (secrets never written to files)

**Files Changed**:
- `.github/workflows/azure-deploy.yml` (lines 77-84 for staging, lines 113-180 for production)

### 2. Added Deployment Prerequisites Verification

**Problem**: Deployments could proceed with missing secrets, leading to runtime failures.

**Solution**: Added verification steps that check all required secrets are configured before deployment:
- Validates presence of database connection string
- Validates presence of JWT secret key
- Validates presence of OpenAI API key
- Validates presence of Azure credentials
- Fails fast with clear error messages if any secret is missing

**Benefits**:
- Prevents incomplete deployments
- Provides early feedback on configuration issues
- Reduces troubleshooting time

### 3. Improved Health Check Diagnostics

**Problem**: Health checks failed silently with no useful diagnostic information.

**Solution**: Enhanced health checks with:
- Detailed HTTP error code reporting
- Error code explanations (500, 502, 503, etc.)
- Multiple retry attempts (10 for production, 5 for staging)
- Root endpoint fallback testing
- Comprehensive troubleshooting instructions
- Verbose curl output for debugging

**Benefits**:
- Better visibility into deployment issues
- Actionable error messages
- Guidance for manual troubleshooting

### 4. Added Azure Login Step

**Problem**: The `azure/appservice-settings@v1` action requires Azure authentication.

**Solution**: Added `azure/login@v1` step before configuring App Service settings for both staging and production environments.

## Configuration Mapping

### Old Approach (File-Based)
```yaml
cat > ./publish/appsettings.Production.json << 'EOF'
{
  "ConnectionStrings": {
    "DefaultConnection": "${{ secrets.DATABASE_CONNECTION_STRING }}"
  }
}
EOF
```
❌ Single quotes prevent variable interpolation
❌ Secrets written to files (less secure)
❌ Files included in deployment artifacts

### New Approach (Environment Variables)
```yaml
- name: Configure Azure App Service Settings (Production)
  uses: azure/appservice-settings@v1
  with:
    app-name: ${{ env.AZURE_WEBAPP_NAME }}
    app-settings-json: |
      [
        {
          "name": "ConnectionStrings__DefaultConnection",
          "value": "${{ secrets.DATABASE_CONNECTION_STRING }}",
          "slotSetting": false
        }
      ]
```
✅ Proper variable interpolation
✅ Secrets managed securely by Azure
✅ No files created
✅ Runtime configuration updates possible

## Environment Variable Naming Convention

Azure App Service uses double underscores (`__`) to represent nested configuration sections:

| Environment Variable | Configuration Path |
|---------------------|-------------------|
| `ConnectionStrings__DefaultConnection` | `ConnectionStrings:DefaultConnection` |
| `JwtSettings__SecretKey` | `JwtSettings:SecretKey` |
| `JwtSettings__Issuer` | `JwtSettings:Issuer` |
| `JwtSettings__Audience` | `JwtSettings:Audience` |
| `OpenAi__ApiKey` | `OpenAi:ApiKey` |

ASP.NET Core's configuration system automatically reads these environment variables and overrides values from `appsettings.json`.

## Required Secrets

### Production
- `AZURE_CREDENTIALS` - Azure Service Principal credentials
- `AZURE_WEBAPP_PUBLISH_PROFILE_PRODUCTION` - App Service publish profile
- `DATABASE_CONNECTION_STRING` - Production database connection
- `JWT_SECRET_KEY` - JWT signing key
- `OPENAI_API_KEY` - OpenAI API key

### Staging
- `AZURE_CREDENTIALS_STAGING` - Azure Service Principal credentials for staging
- `AZURE_WEBAPP_PUBLISH_PROFILE_STAGING` - Staging publish profile
- `STAGING_DATABASE_CONNECTION_STRING` - Staging database connection
- `STAGING_JWT_SECRET_KEY` - Staging JWT key
- `STAGING_OPENAI_API_KEY` - Staging OpenAI key

## Expected Outcomes

After these fixes:
- ✅ Workflow builds successfully without syntax errors
- ✅ Application secrets are properly injected into Azure App Service
- ✅ App starts successfully without HTTP 500.30 errors
- ✅ Health checks pass and confirm app is running
- ✅ Deployment follows Azure best practices
- ✅ Configuration can be updated at runtime without redeployment
- ✅ Better error diagnostics and troubleshooting guidance

## Testing

To test the workflow:

1. **Configure Secrets**: Add all required secrets in GitHub repository settings
2. **Push to Branch**: 
   - Push to `develop` for staging deployment
   - Push to `main` for production deployment
3. **Monitor Workflow**: Check GitHub Actions tab for workflow progress
4. **Verify Deployment**: 
   - Check health check results in workflow logs
   - Verify app is accessible at the deployed URL
   - Check Azure Portal Application Settings are configured correctly

## Rollback Plan

If issues occur:
1. The old `appsettings.Production.json` file with token placeholders remains in the repository
2. Azure Portal Application Settings can be manually configured or removed
3. Previous deployment artifacts are preserved in GitHub Actions

## Documentation

See `DEPLOYMENT.md` for:
- Complete deployment guide
- Azure App Service configuration instructions
- Troubleshooting steps
- Security best practices

## Breaking Changes

⚠️ **Important**: Deployments now require additional GitHub secrets:
- `AZURE_CREDENTIALS` (or `AZURE_CREDENTIALS_STAGING` for staging)

These must be configured before the workflow can run successfully.

## Migration Steps

If you have an existing deployment:

1. **Configure GitHub Secrets**: Add all required secrets listed above
2. **Create Azure Service Principal**: Use Azure CLI to create credentials
3. **Test Staging First**: Deploy to staging environment first to validate changes
4. **Verify Configuration**: Check Azure Portal Application Settings after deployment
5. **Monitor Logs**: Watch Azure App Service logs during first deployment

## Related Issues

Fixes:
- ❌ HTTP 500.30 startup errors (missing configuration)
- ❌ Secret interpolation failures (single quote heredoc issue)
- ❌ Silent health check failures
- ❌ File-based configuration security concerns

## References

- [Azure App Service Configuration](https://docs.microsoft.com/azure/app-service/configure-common)
- [ASP.NET Core Configuration](https://docs.microsoft.com/aspnet/core/fundamentals/configuration/)
- [GitHub Actions for Azure](https://github.com/Azure/actions)
