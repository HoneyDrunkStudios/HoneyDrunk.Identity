# Real DacFx models reproduce equal reports with different SQL. Only Azure/CLI
# transport is replaced; no database, network authentication or token is used.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$version = (Get-Content "$PSScriptRoot/../dotnet-tools.json" -Raw | ConvertFrom-Json).tools.'microsoft.sqlpackage'.version
$nugetRoot = (& dotnet nuget locals global-packages --list).Split(':', 2)[1].Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot locate restored SqlPackage.' }
$dacDirectory = Join-Path $nugetRoot "microsoft.sqlpackage/$version/tools/net10.0/any"
Add-Type -Path (Join-Path $dacDirectory 'Microsoft.SqlServer.Dac.dll')
Add-Type -Path (Join-Path $dacDirectory 'Microsoft.SqlServer.Dac.Extensions.dll')

function New-TestPackage([string]$Sql) {
    $model = [Microsoft.SqlServer.Dac.Model.TSqlModel]::new(
        [Microsoft.SqlServer.Dac.Model.SqlServerVersion]::SqlAzure,
        [Microsoft.SqlServer.Dac.Model.TSqlModelOptions]::new())
    try {
        $model.AddObjects($Sql)
        $stream = [IO.MemoryStream]::new()
        $metadata = [Microsoft.SqlServer.Dac.PackageMetadata]::new()
        $metadata.Name = 'OfflineReview'
        $metadata.Version = '1.0.0.0'
        [Microsoft.SqlServer.Dac.DacPackageExtensions]::BuildPackage($stream, $model, $metadata)
        $stream.Position = 0
        return @{ Package = [Microsoft.SqlServer.Dac.DacPackage]::Load($stream); Stream = $stream }
    } finally { $model.Dispose() }
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('identity-schema-review-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$source = New-TestPackage 'CREATE TABLE dbo.Example (Id int NOT NULL PRIMARY KEY, First nvarchar(100) NULL, Second nvarchar(100) NULL);'
$targetA = New-TestPackage 'CREATE TABLE dbo.Example (Id int NOT NULL PRIMARY KEY, First nvarchar(50) NULL, Second nvarchar(100) NULL);'
$targetB = New-TestPackage 'CREATE TABLE dbo.Example (Id int NOT NULL PRIMARY KEY, First nvarchar(50) NULL, Second nvarchar(50) NULL);'
$options = [Microsoft.SqlServer.Dac.DacDeployOptions]::new()
$options.BlockOnPossibleDataLoss = $true
$options.DropObjectsNotInSource = $false
$options.ScriptDatabaseOptions = $false
$options.IncludeTransactionalScripts = $true
$options.IgnorePermissions = $true
$options.IgnoreRoleMembership = $true
$options.ExcludeObjectTypes = @('Users', 'Logins', 'RoleMembership', 'Permissions')
$database = 'sqldb-hd-identity-dev'
$global:IdentityReviewTest = @{
    Operations = [Collections.Generic.List[string]]::new()
    CurrentTarget = 'A'
    A = @{
        Script = [Microsoft.SqlServer.Dac.DacServices]::GenerateDeployScript($source.Package, $targetA.Package, $database, $options)
        DeployReport = [Microsoft.SqlServer.Dac.DacServices]::GenerateDeployReport($source.Package, $targetA.Package, $database, $options)
    }
    B = @{
        Script = [Microsoft.SqlServer.Dac.DacServices]::GenerateDeployScript($source.Package, $targetB.Package, $database, $options)
        DeployReport = [Microsoft.SqlServer.Dac.DacServices]::GenerateDeployReport($source.Package, $targetB.Package, $database, $options)
    }
}

function az { $global:LASTEXITCODE = 0; return 'offline-test-token' }
function dotnet {
    $action = ($args | Where-Object { $_ -like '/Action:*' }).Substring(8)
    $global:IdentityReviewTest.Operations.Add($action)
    if ($action -notin @('Script', 'DeployReport')) { throw "Unreviewed SQL execution attempted: $action" }
    if (@($args | Where-Object { $_ -like '/SourceFile:*' }).Count -ne 1) { throw 'Missing standalone source argument.' }
    foreach ($flag in @('/p:BlockOnPossibleDataLoss=True', '/p:DropObjectsNotInSource=False',
        '/p:ScriptDatabaseOptions=False', '/p:IncludeTransactionalScripts=True', '/p:IgnorePermissions=True',
        '/p:IgnoreRoleMembership=True', '/p:ExcludeObjectTypes=Users;Logins;RoleMembership;Permissions')) {
        if ($args -notcontains $flag) { throw "Missing safety option $flag" }
    }
    $output = ($args | Where-Object { $_ -like '/OutputPath:*' }).Substring(12)
    [IO.File]::WriteAllText($output, $global:IdentityReviewTest[$global:IdentityReviewTest.CurrentTarget][$action])
    $global:LASTEXITCODE = 0
}

function Assert-Rejected([hashtable]$Arguments, [string]$Message) {
    try { & "$PSScriptRoot/Review-DevDatabase.ps1" @Arguments -Action Publish }
    catch { if ($_.Exception.Message -notlike $Message) { throw }; return }
    throw 'Expected publication rejection.'
}

try {
    if ($global:IdentityReviewTest.A.DeployReport -cne $global:IdentityReviewTest.B.DeployReport) {
        throw 'Regression must demonstrate byte-identical real DacFx reports.'
    }
    if ($global:IdentityReviewTest.A.Script -ceq $global:IdentityReviewTest.B.Script -or
        $global:IdentityReviewTest.B.Script -notmatch 'ALTER COLUMN \[Second\]') {
        throw 'Regression must demonstrate different real DacFx SQL for the same reports.'
    }
    $package = Join-Path $testRoot 'test.dacpac'
    [IO.File]::WriteAllBytes($package, $source.Stream.ToArray())
    $arguments = @{
        Server = 'sql-hd-identity-dev.database.windows.net'; Package = $package
        ReviewDirectory = $testRoot; SourceRevision = ('a' * 40)
    }
    & "$PSScriptRoot/Review-DevDatabase.ps1" @arguments
    if (($global:IdentityReviewTest.Operations -join ',') -ne 'Script,DeployReport') { throw 'Plan must not publish.' }
    Assert-Rejected $arguments 'Schema publication is disabled*'
    $global:IdentityReviewTest.CurrentTarget = 'B'
    Assert-Rejected $arguments 'Schema drift*'
    Write-Output 'PASS: real DacFx identical reports/different ALTER COLUMN scripts; stale target B rejected.'

    $global:IdentityReviewTest.CurrentTarget = 'A'
    foreach ($artifact in @('deploy.sql', 'deploy-report.xml', 'test.dacpac')) {
        $path = Join-Path $testRoot $artifact
        $original = [IO.File]::ReadAllBytes($path)
        [IO.File]::WriteAllText($path, 'tampered')
        Assert-Rejected $arguments 'Reviewed target*'
        [IO.File]::WriteAllBytes($path, $original)
    }
    $changedRevision = $arguments.Clone()
    $changedRevision.SourceRevision = 'b' * 40
    Assert-Rejected $changedRevision 'Reviewed target*'
    Write-Output 'PASS: script/report/DACPAC/revision tampering rejected; publication remains fail-closed.'
} finally {
    foreach ($item in @($source, $targetA, $targetB)) { $item.Package.Dispose(); $item.Stream.Dispose() }
    Remove-Variable -Name IdentityReviewTest -Scope Global
}
