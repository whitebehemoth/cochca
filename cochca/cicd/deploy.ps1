param(
    [Parameter(Mandatory=$true)]
    [string]$SubscriptionId,
    
    [string]$ResourceGroup = "cochca-rg",
    [string]$Location = "eastus2",
    [string]$AppName = "cochca",
    [string]$AcrName = "cochcaacr",
    [string]$AcrSku = "Basic",
    [string]$ParametersFile = "main.parameters.json",
    
    [Parameter(Mandatory=$true)]
    [string]$TurnDomain,
    
    [string]$CustomDomain = "",
    
    [switch]$SkipVM
)


Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Az {
    param([string]$Command)
    Write-Host "[AZ] $Command"
    $output = Invoke-Expression $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $Command"
    }
    return $output
}

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptRoot
$ParametersPath = Join-Path $ScriptRoot $ParametersFile
$BicepPath = Join-Path $ScriptRoot "main.bicep"
$ProjectPath = Join-Path $RepoRoot "cochca.csproj"

Write-Host "`n=== DEPLOY START ===" -ForegroundColor Cyan
Write-Host "[PATH] ScriptRoot: $ScriptRoot"
Write-Host "[PATH] RepoRoot: $RepoRoot"
Write-Host "[PATH] ProjectPath: $ProjectPath"

if (-not (Test-Path $ProjectPath)) {
    throw "Project file not found: $ProjectPath"
}
Write-Host "[OK] Project file found" -ForegroundColor Green

if ($SubscriptionId) {
    Write-Host "[STEP 1/5] Setting subscription: $SubscriptionId"
    Invoke-Az "az account set --subscription $SubscriptionId"
}

Write-Host "[STEP 2/5] Creating resource group: $ResourceGroup in $Location"
Invoke-Az "az group create --name $ResourceGroup --location $Location"

Write-Host "[STEP 3/5] Checking Azure Container Registry: $AcrName"
$acr = az acr show --name $AcrName --resource-group $ResourceGroup 2>$null | ConvertFrom-Json
if (-not $acr) {
    Write-Host "[INFO] ACR not found, creating..." -ForegroundColor Yellow
    Invoke-Az "az acr create --name $AcrName --resource-group $ResourceGroup --location $Location --sku $AcrSku --admin-enabled true"
} else {
    Write-Host "[OK] ACR exists" -ForegroundColor Green
}

Write-Host "[STEP 4/5] Getting ACR credentials"
$acrLoginServer = (az acr show --name $AcrName --resource-group $ResourceGroup --query loginServer -o tsv).Trim()
$acrCreds = az acr credential show --name $AcrName --resource-group $ResourceGroup | ConvertFrom-Json
$registryUser = $acrCreds.username
$registryPassword = $acrCreds.passwords[0].value

Write-Host "[INFO] Registry: $acrLoginServer"
Write-Host "[INFO] Username: $registryUser"
Write-Host "[DEBUG] ACR variables check:" -ForegroundColor Gray
Write-Host "  AppName (from param): '$AppName'" -ForegroundColor Gray
Write-Host "  AcrName (from param): '$AcrName'" -ForegroundColor Gray

Write-Host "[STEP 4.5/5] Building and publishing container to ACR..."
Write-Host "[INFO] Using direct credential authentication (no Docker required)" -ForegroundColor Yellow
Write-Host "[DOTNET] Publishing project: $ProjectPath" -ForegroundColor Cyan

# Create temporary Docker config with embedded credentials
$tempDockerConfig = Join-Path $env:TEMP "docker-config-$(Get-Random)"
New-Item -ItemType Directory -Path $tempDockerConfig -Force | Out-Null

# Encode credentials in base64 (username:password)
$authString = "${registryUser}:${registryPassword}"
$authBytes = [System.Text.Encoding]::UTF8.GetBytes($authString)
$authBase64 = [System.Convert]::ToBase64String($authBytes)

# Create Docker config.json with direct auth
$dockerConfig = @{
    auths = @{
        $acrLoginServer = @{
            auth = $authBase64
        }
    }
} | ConvertTo-Json -Depth 10

Set-Content "$tempDockerConfig\config.json" -Value $dockerConfig
Write-Host "[INFO] Docker config created: $tempDockerConfig\config.json" -ForegroundColor Yellow

$env:DOCKER_CONFIG = $tempDockerConfig

try {
    dotnet publish $ProjectPath -c Release -t:PublishContainer `
      -p ContainerRegistry=$acrLoginServer `
      -p ContainerRepository=$AppName `
      -p ContainerImageTag=latest

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
} finally {
    # Cleanup temp config
    Remove-Item $tempDockerConfig -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Env:\DOCKER_CONFIG -ErrorAction SilentlyContinue
}

Write-Host "[DEBUG] Before fullImage creation:" -ForegroundColor Gray
Write-Host "  acrLoginServer = '$acrLoginServer' (length: $($acrLoginServer.Length))" -ForegroundColor Gray
Write-Host "  AppName = '$AppName' (length: $($AppName.Length))" -ForegroundColor Gray

if ([string]::IsNullOrWhiteSpace($AppName)) {
    throw "AppName is null or empty! Cannot create fullImage."
}
if ([string]::IsNullOrWhiteSpace($acrLoginServer)) {
    throw "acrLoginServer is null or empty! Cannot create fullImage."
}

$fullImage = "{0}/{1}:latest" -f $acrLoginServer, $AppName

Write-Host "[DEBUG] After fullImage creation:" -ForegroundColor Gray
Write-Host "  fullImage = '$fullImage' (length: $($fullImage.Length))" -ForegroundColor Gray
Write-Host "[OK] Image published: $fullImage" -ForegroundColor Green

Write-Host "[STEP 4.8/5] Generating TURN server password"
# Generate secure random password for TURN
$turnPassword = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 32 | ForEach-Object {[char]$_})
Write-Host "[INFO] TURN password generated (will be passed to VM and Container App)" -ForegroundColor Yellow

Write-Host "[STEP 5/5] Deploying Bicep template"
Write-Host "[INFO] Template: $BicepPath"
Write-Host "[INFO] Parameters: $ParametersPath"
Write-Host "[INFO] Container image: $fullImage"
Write-Host "[DEBUG] Full command parameters:" -ForegroundColor Gray
Write-Host "  - appName: $AppName" -ForegroundColor Gray
Write-Host "  - containerImage: $fullImage" -ForegroundColor Gray
Write-Host "  - location: $Location" -ForegroundColor Gray
Write-Host "  - registryServer: $acrLoginServer" -ForegroundColor Gray
Write-Host "  - registryUsername: $registryUser" -ForegroundColor Gray

# Convert deployVM to string for Azure CLI
$deployVMValue = if ($SkipVM) { "false" } else { "true" }
Write-Host "  - deployVM: $deployVMValue" -ForegroundColor Gray

az deployment group create `
--resource-group $ResourceGroup `
--template-file $BicepPath `
--parameters @$ParametersPath `
--parameters appName=$AppName containerImage=$fullImage location=$Location `
--parameters registryServer=$acrLoginServer registryUsername=$registryUser registryPassword=$registryPassword `
--parameters turnPassword=$turnPassword turnDomain=$TurnDomain customDomain=$CustomDomain `
--parameters deployVM=$deployVMValue

if ($LASTEXITCODE -ne 0) {
    throw "Deployment failed with exit code $LASTEXITCODE"
}

Write-Host "`n=== DEPLOY SUCCESS ===" -ForegroundColor Green
