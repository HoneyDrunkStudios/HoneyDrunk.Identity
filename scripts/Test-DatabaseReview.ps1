# Offline tests for the review/publish boundary. No database, Azure call or token is used.
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('identity-schema-review-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$package = Join-Path $testRoot 'test.dacpac'
Set-Content -LiteralPath $package 'test-package'
$global:IdentitySchemaTestOperations = [Collections.Generic.List[string]]::new()
$global:IdentitySchemaTestDrift = $false
function az { $global:LASTEXITCODE = 0; return 'offline-test-token' }
function dotnet {
    $action = ($args | Where-Object { $_ -like '/Action:*' }).Substring(8)
    $global:IdentitySchemaTestOperations.Add($action)
    if (@($args | Where-Object { $_ -like '/SourceFile:*' }).Count -ne 1) { throw 'Missing standalone source argument.' }
    if ($args -notcontains '/p:BlockOnPossibleDataLoss=True' -or $args -notcontains '/p:DropObjectsNotInSource=False') { throw 'Missing safety flags.' }
    if ($action -ne 'Publish') {
        $output = ($args | Where-Object { $_ -like '/OutputPath:*' }).Substring(12)
        Set-Content -LiteralPath $output -Value $(if ($global:IdentitySchemaTestDrift) { 'changed-plan' } else { $action })
    }
    $global:LASTEXITCODE = 0
}
$arguments = @{
    Server = 'sql-hd-identity-dev.database.windows.net'; Package = $package
    ReviewDirectory = $testRoot; SourceRevision = ('a' * 40)
}
& "$PSScriptRoot/Review-DevDatabase.ps1" @arguments
if (($global:IdentitySchemaTestOperations -join ',') -ne 'Script,DeployReport') { throw 'Plan must not publish.' }
& "$PSScriptRoot/Review-DevDatabase.ps1" @arguments -Action Publish
if ($global:IdentitySchemaTestOperations[-1] -ne 'Publish') { throw 'Unchanged reviewed plan should publish.' }
$global:IdentitySchemaTestDrift = $true
try { & "$PSScriptRoot/Review-DevDatabase.ps1" @arguments -Action Publish; throw 'Expected drift rejection.' }
catch { if ($_.Exception.Message -notlike 'Schema drift*') { throw } }
Set-Content -LiteralPath $package 'tampered-package'
try { & "$PSScriptRoot/Review-DevDatabase.ps1" @arguments -Action Publish; throw 'Expected artifact rejection.' }
catch { if ($_.Exception.Message -notlike 'Reviewed target*') { throw } }
if (@($global:IdentitySchemaTestOperations | Where-Object { $_ -eq 'Publish' }).Count -ne 1) { throw 'Rejected plans reached Publish.' }
Write-Output 'PASS: plan-only, reviewed publish, drift rejection and artifact rejection (offline).'
