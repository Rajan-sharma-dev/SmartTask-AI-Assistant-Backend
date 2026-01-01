# Quick fix script to configure Azure App Service immediately
# Run this to fix the 500.30 error without waiting for deployment

Write-Host "?? Quick Fix for Azure App Service Configuration" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan

# Check Azure CLI
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Host "? Azure CLI not found." -ForegroundColor Red
    exit 1
}

# Check login
try {
    $account = az account show 2>$null | ConvertFrom-Json
    if (-not $account) {
        Write-Host "Logging into Azure CLI..." -ForegroundColor Yellow
        az login
    }
    Write-Host "? Authenticated with Azure" -ForegroundColor Green
} catch {
    Write-Host "? Failed to authenticate" -ForegroundColor Red
    exit 1
}

# Get the secrets from the user
Write-Host ""
Write-Host "Enter your production secrets:" -ForegroundColor Yellow
Write-Host "(These should be the same values you configured in GitHub Secrets)" -ForegroundColor Cyan

$dbConnectionString = Read-Host -Prompt "DATABASE_CONNECTION_STRING"
$jwtSecretKey = Read-Host -Prompt "JWT_SECRET_KEY"
$openAiApiKey = Read-Host -Prompt "OPENAI_API_KEY"

if ([string]::IsNullOrEmpty($dbConnectionString) -or [string]::IsNullOrEmpty($jwtSecretKey) -or [string]::IsNullOrEmpty($openAiApiKey)) {
    Write-Host "? All three secrets are required" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "?? Configuring Azure App Service environment variables..." -ForegroundColor Yellow

try {
    # Configure App Service settings directly
    az webapp config appsettings set `
        --name "smarttask-ai-api" `
        --resource-group "smarttask-ai-rg-production" `
        --settings `
        "ASPNETCORE_ENVIRONMENT=Production" `
        "ConnectionStrings__DefaultConnection=$dbConnectionString" `
        "JwtSettings__SecretKey=$jwtSecretKey" `
        "JwtSettings__Issuer=SmartTask-AI-Production" `
        "JwtSettings__Audience=SmartTask-AI-Users" `
        "JwtSettings__AccessTokenExpirationMinutes=60" `
        "JwtSettings__RefreshTokenExpirationDays=7" `
        "OpenAi__ApiKey=$openAiApiKey" `
        "OpenAi__BaseUrl=https://api.openai.com/v1" `
        "OpenAi__DefaultModel=gpt-4o-mini" `
        "OpenAi__MaxTokens=1500" `
        "OpenAi__Temperature=0.7" `
        "OpenAi__TimeoutSeconds=30" `
        "Logging__LogLevel__Default=Information"
        
    Write-Host "? App Service settings configured successfully!" -ForegroundColor Green
    
    Write-Host ""
    Write-Host "?? Restarting the app to apply new settings..." -ForegroundColor Yellow
    az webapp restart --name "smarttask-ai-api" --resource-group "smarttask-ai-rg-production"
    
    Write-Host "? App restarted successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "? Waiting for app to start..." -ForegroundColor Yellow
    
    # Wait for app to start
    Start-Sleep -Seconds 45
    
    # Test the health endpoint
    $healthUrl = "https://smarttask-ai-api-h7evagcdc3e2hrek.centralindia-01.azurewebsites.net/health"
    Write-Host "?? Testing health endpoint: $healthUrl" -ForegroundColor Cyan
    
    try {
        $response = Invoke-WebRequest -Uri $healthUrl -Method GET -TimeoutSec 30 -UseBasicParsing
        Write-Host "? SUCCESS: App is now working! Status: $($response.StatusCode)" -ForegroundColor Green
        
        if ($response.Content) {
            Write-Host "Health Response:" -ForegroundColor Cyan
            Write-Host $response.Content -ForegroundColor White
        }
        
        Write-Host ""
        Write-Host "?? Your app is now live and working!" -ForegroundColor Green
        Write-Host "?? App URL: https://smarttask-ai-api-h7evagcdc3e2hrek.centralindia-01.azurewebsites.net" -ForegroundColor Cyan
        
    } catch {
        Write-Host "?? Health check didn't respond, but configuration was applied." -ForegroundColor Yellow
        Write-Host "The app may need a few more minutes to start." -ForegroundColor Yellow
        Write-Host "Try accessing it manually: $healthUrl" -ForegroundColor Cyan
    }
    
} catch {
    Write-Host "? Error configuring App Service: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "? Configuration complete!" -ForegroundColor Green
Write-Host "Your GitHub Actions workflow has also been fixed for future deployments." -ForegroundColor Cyan