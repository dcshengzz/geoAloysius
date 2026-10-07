#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Publish GpsSync.Api and stand it up under IIS in one shot (guide Steps 2-4 + 8).

.DESCRIPTION
    Run in an ELEVATED PowerShell on the Windows Server. Prerequisites (guide Step 0):
      - IIS installed, then the .NET 10 Hosting Bundle installed, then `net stop was /y && net start w3svc`
      - .NET 10 SDK on PATH (so `dotnet publish` works here), or publish elsewhere and point -PublishPath at the copied folder
    The script is RE-RUNNABLE: it updates the app pool / site if they already exist,
    and it preserves an existing Jwt:Key across re-publishes.

.EXAMPLE
    .\deploy-iis.ps1
    .\deploy-iis.ps1 -PackagePath ..\app        # from the pre-built zip (no .NET SDK needed)
    .\deploy-iis.ps1 -Port 5080 -JwtKey (openssl rand -base64 48)
    .\deploy-iis.ps1 -ConnectionString "Server=localhost;Database=GpsSync;User Id=gpssync;Password=****;TrustServerCertificate=True;Encrypt=False"
#>
[CmdletBinding()]
param(
    [string]$ProjectPath      = (Join-Path $PSScriptRoot '..\GpsSync.Api\GpsSync.Api.csproj'),
    [string]$PublishPath      = 'C:\inetpub\GpsSyncApi',
    [string]$SiteName         = 'GpsSyncApi',
    [string]$AppPoolName      = 'GpsSyncApi',
    [int]   $Port             = 5080,
    [string]$JwtKey           = $null,   # if omitted: keep the existing key, else generate one
    [string]$ConnectionString = $null,   # if omitted: keep whatever appsettings.Production.json ships
    [string]$PackagePath      = $null    # pre-built publish folder (e.g. from the zip): copied instead of running dotnet publish
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration -ErrorAction Stop
function Info($m){ Write-Host "==> $m" -ForegroundColor Cyan }
function Ok  ($m){ Write-Host "    $m" -ForegroundColor Green }
function Warn($m){ Write-Host "    $m" -ForegroundColor Yellow }

$prodJson = Join-Path $PublishPath 'appsettings.Production.json'

# --- Preserve an existing real Jwt:Key so a re-publish doesn't wipe it ---------
$existingKey = $null
if (Test-Path $prodJson) {
    try { $existingKey = (Get-Content $prodJson -Raw | ConvertFrom-Json).Jwt.Key } catch {}
}

# --- 1. Publish ----------------------------------------------------------------
if ($PackagePath) {
    Info "Copying pre-built app $PackagePath  ->  $PublishPath"
    New-Item -ItemType Directory -Force -Path $PublishPath | Out-Null
    if (Get-WebAppPoolState -Name $AppPoolName -ErrorAction SilentlyContinue) { Stop-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue; Start-Sleep 2 }  # release locked DLLs
    # Keep an existing appsettings.Production.json (it holds the real Jwt:Key / connection string).
    $exclude = if (Test-Path $prodJson) { @('appsettings.Production.json') } else { @() }
    Copy-Item (Join-Path $PackagePath '*') $PublishPath -Recurse -Force -Exclude $exclude
} else {
    Info "Publishing $ProjectPath  ->  $PublishPath  (Release)"
    dotnet publish $ProjectPath -c Release -o $PublishPath
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (is the .NET 10 SDK on PATH?)." }
}
New-Item -ItemType Directory -Force -Path (Join-Path $PublishPath 'logs') | Out-Null
Ok "Publish complete."

# --- 2. Patch appsettings.Production.json (key + optional connection string) ----
$json = Get-Content $prodJson -Raw | ConvertFrom-Json

$placeholder = 'PASTE_A_LONG_RANDOM_SECRET'
if ($JwtKey) {
    $key = $JwtKey
} elseif ($existingKey -and ($existingKey -notmatch $placeholder) -and $existingKey.Length -ge 32) {
    $key = $existingKey                              # keep the key from the previous deploy
} else {
    $key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    Warn "Generated a new Jwt:Key (all existing tokens are invalidated)."
}
$json.Jwt.Key = $key
if ($ConnectionString) { $json.ConnectionStrings.Default = $ConnectionString }
($json | ConvertTo-Json -Depth 10) | Set-Content $prodJson -Encoding UTF8
Ok "appsettings.Production.json configured (Jwt:Key set$(if($ConnectionString){', connection string overridden'}))."

# --- 3. IIS: app pool ----------------------------------------------------------

if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    Info "Creating app pool '$AppPoolName'"
    New-WebAppPool -Name $AppPoolName | Out-Null
} else {
    Info "App pool '$AppPoolName' already exists — updating settings"
}
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion    -Value ''                  # "No Managed Code"
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode                -Value 'AlwaysRunning'
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.idleTimeout -Value ([TimeSpan]'00:00:00')
Ok "App pool: No Managed Code, AlwaysRunning, idle timeout disabled."

# --- 4. Grant the app-pool identity access to the folder -----------------------
$who = "IIS AppPool\$AppPoolName"
& icacls $PublishPath                       /grant "${who}:(OI)(CI)(RX)" /T | Out-Null   # read+execute app
& icacls (Join-Path $PublishPath 'logs')    /grant "${who}:(OI)(CI)(M)"  /T | Out-Null   # write stdout log
Ok "Granted '$who' access to $PublishPath."

# --- 5. IIS: site --------------------------------------------------------------
if (-not (Test-Path "IIS:\Sites\$SiteName")) {
    Info "Creating site '$SiteName' on http port $Port"
    New-Website -Name $SiteName -PhysicalPath $PublishPath -ApplicationPool $AppPoolName -Port $Port | Out-Null
} else {
    Info "Site '$SiteName' already exists — updating path / pool"
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath    -Value $PublishPath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
}
Set-ItemProperty "IIS:\Sites\$SiteName" -Name serverAutoStart -Value $true

# --- 6. Start ------------------------------------------------------------------
Restart-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
Start-Website       -Name $SiteName    -ErrorAction SilentlyContinue
Ok "Site started."

Write-Host ""
Write-Host "NEXT STEPS" -ForegroundColor Yellow
Write-Host "  1. In SSMS, allow the app pool to reach SQL (Windows auth):"       -ForegroundColor Yellow
Write-Host "       CREATE LOGIN [$who] FROM WINDOWS;"                             -ForegroundColor Gray
Write-Host "       ALTER SERVER ROLE dbcreator ADD MEMBER [$who];"               -ForegroundColor Gray
Write-Host "  2. Test locally:   curl http://localhost:$Port/health"             -ForegroundColor Yellow
Write-Host "  3. Run gpssync-maintenance.sql once (indexes + nightly retention)." -ForegroundColor Yellow
Write-Host "  4. Expose over HTTPS:   tailscale funnel --bg http://localhost:$Port" -ForegroundColor Yellow
Write-Host "  5. Set AppConfig.cs (Release) ApiBaseUrl to your https://<name>.ts.net, rebuild the APK." -ForegroundColor Yellow
