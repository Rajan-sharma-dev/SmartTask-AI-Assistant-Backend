# Quick Setup Guide

This guide provides the minimum steps needed to deploy the SmartTask AI Assistant Backend to Azure.

## Prerequisites

- Azure subscription with an App Service created
- GitHub repository with Actions enabled
- Azure CLI installed (for creating service principal)

## Step 1: Configure GitHub Secrets

Go to your GitHub repository → Settings → Secrets and variables → Actions

### Required Secrets for Production

Click "New repository secret" and add each of these:

1. **AZURE_CREDENTIALS**
   ```bash
   # Run this command to create the service principal:
   az ad sp create-for-rbac \
     --name "smarttask-ai-github-actions" \
     --role contributor \
     --scopes /subscriptions/{subscription-id}/resourceGroups/{resource-group} \
     --sdk-auth
   
   # Copy the entire JSON output as the secret value
   ```

2. **AZURE_WEBAPP_PUBLISH_PROFILE_PRODUCTION**
   - Go to Azure Portal → Your App Service → Overview
   - Click "Get publish profile"
   - Copy the entire XML content as the secret value

3. **DATABASE_CONNECTION_STRING**
   - Example: `Server=your-server.database.windows.net;Database=SmartTaskDB;User Id=admin;Password=YourPassword;`
   - Must be a valid Azure SQL connection string

4. **JWT_SECRET_KEY**
   - Must be at least 32 characters long
   - Example: `YourSuperSecretKeyThatIsAtLeast32CharactersLongForMaximumSecurity123!`
   - Generate a secure random string

5. **OPENAI_API_KEY**
   - Get from https://platform.openai.com/api-keys
   - Format: `sk-...`

### Optional: Secrets for Staging

If you want staging deployments (deploys on push to `develop` branch):

1. **AZURE_CREDENTIALS_STAGING** - Same format as production, different scope
2. **AZURE_WEBAPP_PUBLISH_PROFILE_STAGING** - From staging App Service
3. **STAGING_DATABASE_CONNECTION_STRING** - Staging database
4. **STAGING_JWT_SECRET_KEY** - Different key for staging
5. **STAGING_OPENAI_API_KEY** - Can be same or different from production

## Step 2: Verify Workflow File

The workflow file `.github/workflows/azure-deploy.yml` is already configured with all necessary steps.

Key features:
- ✅ Builds .NET 8.0 application
- ✅ Configures Azure App Service settings with secrets
- ✅ Validates all required secrets before deployment
- ✅ Deploys to staging (develop branch) or production (main branch)
- ✅ Runs health checks with detailed diagnostics

## Step 3: Test Deployment

### For Production Deployment:
```bash
git checkout main
git add .
git commit -m "Your changes"
git push origin main
```

### For Staging Deployment:
```bash
git checkout develop
git add .
git commit -m "Your changes"
git push origin develop
```

### Monitor Deployment:
1. Go to GitHub repository → Actions tab
2. Click on the running workflow
3. Watch the logs for each step
4. Check for any errors in:
   - Build and Test
   - Verify deployment prerequisites
   - Configure Azure App Service Settings
   - Deploy to Azure Web App
   - Run health checks

## Step 4: Verify Azure Configuration

After the first successful deployment:

1. Go to Azure Portal → Your App Service → Configuration → Application settings

2. Verify these settings exist:
   ```
   ConnectionStrings__DefaultConnection
   JwtSettings__SecretKey
   JwtSettings__Issuer
   JwtSettings__Audience
   OpenAi__ApiKey
   ASPNETCORE_ENVIRONMENT
   ```

3. If any are missing, the workflow may have failed. Check the workflow logs.

## Step 5: Check Application Health

### Using Browser:
- Navigate to: `https://your-app-name.azurewebsites.net/health`
- Should return HTTP 200 OK

### Using Azure Portal:
1. Go to Your App Service → Log stream
2. Watch for startup messages
3. Look for:
   - ✅ JWT Settings loaded successfully
   - ✅ Database connection string loaded successfully
   - ✅ OpenAI Settings loaded successfully

### If App Fails to Start (HTTP 500.30):

1. **Check Application Logs:**
   ```bash
   az webapp log tail --name your-app-name --resource-group your-resource-group
   ```

2. **Common Issues:**
   - Missing or invalid database connection string
   - Missing JWT secret key
   - Missing OpenAI API key
   - Database not accessible from Azure

3. **Verify Configuration in Azure Portal:**
   - Go to App Service → Configuration
   - Check all application settings are present
   - Click "Save" to restart the app if you make changes

## Troubleshooting

### "Missing required secrets" Error
**Solution:** Configure all required secrets in GitHub (see Step 1)

### "Azure login failed" Error
**Solution:** Verify AZURE_CREDENTIALS is valid JSON with correct subscription ID

### "Health check failed" Error
**Possible causes:**
1. App is still starting up (give it 2-3 minutes)
2. Configuration missing or incorrect
3. Database connection failed
4. OpenAI API key invalid

**Check logs:**
```bash
az webapp log tail --name your-app-name --resource-group your-resource-group
```

### Workflow Doesn't Run
**Check:**
1. Workflow file is in `.github/workflows/` directory
2. You're pushing to `main` (production) or `develop` (staging)
3. GitHub Actions is enabled in repository settings

## Security Notes

⚠️ **Important Security Practices:**

1. ✅ Never commit secrets to the repository
2. ✅ Use different secrets for staging and production
3. ✅ Rotate secrets regularly (every 90 days recommended)
4. ✅ Use strong JWT secret keys (32+ characters)
5. ✅ Restrict database access to Azure App Service IP addresses
6. ✅ Enable HTTPS only in Azure App Service
7. ✅ Consider using Azure Key Vault for enhanced security

## Next Steps

After successful deployment:

1. **Set up monitoring:** Enable Application Insights in Azure
2. **Configure custom domain:** Add your domain to App Service
3. **Set up SSL certificate:** Use App Service managed certificate or upload custom
4. **Configure scaling:** Set up auto-scaling rules based on load
5. **Set up alerts:** Configure alerts for failures and performance issues

## Support

For detailed documentation, see:
- `DEPLOYMENT.md` - Complete deployment guide
- `CHANGELOG-WORKFLOW.md` - Detailed workflow changes
- [Azure App Service Documentation](https://docs.microsoft.com/azure/app-service/)

For issues:
1. Check GitHub Actions workflow logs
2. Check Azure App Service logs
3. Review Application Insights (if enabled)
4. Open an issue in the GitHub repository
