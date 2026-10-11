[CmdletBinding()]
param(
 [ValidateSet('Plan','Initialize')][string]$Action='Plan',
 [Parameter(Mandatory)][string]$Package,
 [Parameter(Mandatory)][string]$ReviewDirectory,
 [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$SourceRevision
)
$ErrorActionPreference='Stop'
Import-Module "$PSScriptRoot/IdentityBootstrap.psm1" -Force
$server='sql-hd-shared-dev.database.windows.net'
$database='sqldb-hd-identity-dev'
$subscription='82073da5-bd6d-4874-947e-73b791054cbc'
$tenant='f5654adb-2a4c-4317-9217-09ef32ccdd3a'
$packagePath=(Resolve-Path -LiteralPath $Package).Path
$reviewPath=[IO.Path]::GetFullPath($ReviewDirectory)
New-Item -ItemType Directory -Path $reviewPath -Force|Out-Null
$site=az webapp show --subscription $subscription -g rg-hd-identity-dev -n app-hd-identity-dev --query '{enabled:enabled,state:state}' -o json|ConvertFrom-Json
if($LASTEXITCODE -or $site.enabled -ne $false -or $site.state -notin @('Disabled','Stopped')){throw 'First initialization requires the disabled app'}
$token=az account get-access-token --tenant $tenant --resource https://database.windows.net/ --query accessToken -o tsv
if($LASTEXITCODE -or !$token){throw 'SQL token acquisition failed'}
$connectionString="Server=tcp:$server,1433;Initial Catalog=$database;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Application Name=IdentityInitialSetup;Pooling=False"
$connection=$null;$guard=$false
try {
 $connection=Open-BootstrapConnection $connectionString $token
 Assert-EmptyBootstrapTarget $connection $database
 $scriptPath=Join-Path $reviewPath 'deploy.sql'
 $reportPath=Join-Path $reviewPath 'deploy-report.xml'
 $manifestPath=Join-Path $reviewPath 'manifest.json'
 if($Action -eq 'Plan'){
  $review=Get-BootstrapReview $connectionString $token $packagePath $database
  $null=@(ConvertTo-BootstrapBatches $review.Script $database)
  [IO.File]::WriteAllText($scriptPath,$review.Script)
  [IO.File]::WriteAllText($reportPath,$review.Report)
  @{server=$server;database=$database;revision=$SourceRevision;packageSha256=(Get-FileHash $packagePath).Hash;scriptSha256=(Get-FileHash $scriptPath).Hash;reportSha256=(Get-FileHash $reportPath).Hash;wrapperSha256=(Get-FileHash $PSCommandPath).Hash;executorSha256=(Get-FileHash "$PSScriptRoot/IdentityBootstrap.psm1").Hash}|ConvertTo-Json|Set-Content $manifestPath
  Write-Output 'Initial schema plan saved; no deployment SQL executed.'
  return
 }
 $m=Get-Content $manifestPath -Raw|ConvertFrom-Json
 if($m.wrapperSha256 -ne (Get-FileHash $PSCommandPath).Hash -or $m.server -ne $server -or $m.database -ne $database -or $m.revision -ne $SourceRevision -or $m.packageSha256 -ne (Get-FileHash $packagePath).Hash -or $m.scriptSha256 -ne (Get-FileHash $scriptPath).Hash -or $m.reportSha256 -ne (Get-FileHash $reportPath).Hash -or $m.executorSha256 -ne (Get-FileHash "$PSScriptRoot/IdentityBootstrap.psm1").Hash){throw 'Reviewed source, target or executor changed'}
 $reviewedScript=[IO.File]::ReadAllText($scriptPath)
 $batches=@(ConvertTo-BootstrapBatches $reviewedScript $database)
 Enter-BootstrapGuard $connection;$guard=$true
 # Fresh planning is read-only. The guard rejects DDL from every other session,
 # including other administrator sessions, throughout comparison and execution.
 $fresh=Get-BootstrapReview $connectionString $token $packagePath $database
 [IO.File]::WriteAllText((Join-Path $reviewPath 'fresh-deploy.sql'),$fresh.Script)
 [IO.File]::WriteAllText((Join-Path $reviewPath 'fresh-report.xml'),$fresh.Report)
 if($fresh.Script -cne $reviewedScript -or $fresh.Report -cne [IO.File]::ReadAllText($reportPath)){throw 'Schema drift changed reviewed SQL/report'}
 Invoke-ExactBootstrap $connection $batches
 @{completedUtc=(Get-Date).ToUniversalTime().ToString('o');scriptSha256=$m.scriptSha256;packageSha256=$m.packageSha256;revision=$SourceRevision;outcome='Schema committed';batches=$batches.Count}|ConvertTo-Json|Set-Content (Join-Path $reviewPath 'execution.json')
 Write-Output 'Reviewed schema committed atomically. Runtime grants remain a separate step.'
} finally {
 try {
  if($guard -and $connection.State -eq 'Open'){Exit-BootstrapGuard $connection}
 } finally {
  if($connection){$connection.Dispose()}
  $token=$null
 }
}
