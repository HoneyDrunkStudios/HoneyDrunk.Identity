[CmdletBinding()]
param(
    [ValidateSet('Plan', 'Publish')][string]$Action = 'Plan',
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+\.database\.windows\.net$')][string]$Server,
    [ValidateSet('sqldb-hd-identity-dev')][string]$Database = 'sqldb-hd-identity-dev',
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][string]$ReviewDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$SourceRevision
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$packagePath = (Resolve-Path -LiteralPath $Package).Path
$reviewPath = [IO.Path]::GetFullPath($ReviewDirectory)
New-Item -ItemType Directory -Path $reviewPath -Force | Out-Null
# Azure CLI is already authenticated by the environment's dedicated OIDC identity.
# Never write the token to disk or echo SqlPackage's command line.
$token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) { throw 'SQL access token acquisition failed.' }
$connection = "Server=tcp:$Server,1433;Initial Catalog=$Database;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30"
$common = @("/SourceFile:$packagePath", "/TargetConnectionString:$connection", "/AccessToken:$token",
    '/p:BlockOnPossibleDataLoss=True', '/p:DropObjectsNotInSource=False', '/p:ScriptDatabaseOptions=False',
    '/p:IncludeTransactionalScripts=True', '/p:IgnorePermissions=True', '/p:IgnoreRoleMembership=True',
    '/p:ExcludeObjectTypes=Users;Logins;RoleMembership;Permissions')

function Invoke-SchemaAction([ValidateSet('Script', 'DeployReport')][string]$Operation, [string]$OutputPath) {
    $arguments = @('tool', 'run', 'sqlpackage', "/Action:$Operation") + $common
    if ($OutputPath) { $arguments += "/OutputPath:$OutputPath" }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "SqlPackage $Operation failed." }
}

try {
    $manifestPath = Join-Path $reviewPath 'manifest.json'
    $reportPath = Join-Path $reviewPath 'deploy-report.xml'
    $scriptPath = Join-Path $reviewPath 'deploy.sql'
    $packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    if ($Action -eq 'Plan') {
        Invoke-SchemaAction 'Script' $scriptPath
        Invoke-SchemaAction 'DeployReport' $reportPath
        @{
            server = $Server; database = $Database; revision = $SourceRevision; packageSha256 = $packageHash
            reportSha256 = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash
            scriptSha256 = (Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash
        } | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8
        return
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.server -ne $Server -or $manifest.database -ne $Database -or $manifest.revision -ne $SourceRevision -or
        $manifest.packageSha256 -ne $packageHash -or
        $manifest.reportSha256 -ne (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash -or
        $manifest.scriptSha256 -ne (Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash) {
        throw 'Reviewed target, source revision, DACPAC, script or report differs. Generate a fresh plan.'
    }
    $currentReport = Join-Path $reviewPath 'current-report.xml'
    $currentScript = Join-Path $reviewPath 'current-deploy.sql'
    Invoke-SchemaAction 'DeployReport' $currentReport
    Invoke-SchemaAction 'Script' $currentScript
    if ((Get-FileHash -LiteralPath $currentReport -Algorithm SHA256).Hash -ne $manifest.reportSha256 -or
        (Get-FileHash -LiteralPath $currentScript -Algorithm SHA256).Hash -ne $manifest.scriptSha256) {
        throw 'Schema drift changed the reviewed plan. Publish refused; generate and review a fresh plan.'
    }
    # Fail closed until execution can use these reviewed bytes while protecting target
    # state through execution. SqlPackage Publish replans; an application lock alone
    # cannot prevent outside DDL. Neither is an acceptable replacement for this hold.
    throw 'Schema publication is disabled pending a reviewed target-state concurrency boundary and exact-script execution. No deployment SQL was executed.'
} finally {
    $token = $null
    $common = $null
}
