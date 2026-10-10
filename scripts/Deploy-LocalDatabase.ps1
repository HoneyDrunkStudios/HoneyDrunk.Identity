[CmdletBinding()]
param([ValidateSet('Script', 'DeployReport', 'Publish')][string]$Action = 'Script')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'HoneyDrunk.Identity/HoneyDrunk.Identity.Database'
Push-Location $repo
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'SqlPackage tool restore failed.' }
    dotnet build (Join-Path $project 'HoneyDrunk.Identity.Database.sqlproj') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'SQL project build failed.' }
    $arguments = @(
        'tool', 'run', 'sqlpackage', "/Action:$Action",
        "/SourceFile:$(Join-Path $project 'bin/Debug/HoneyDrunk.Identity.Database.dacpac')",
        "/Profile:$(Join-Path $project 'PublishProfiles/Local.publish.xml')"
    )
    if ($Action -ne 'Publish') {
        $extension = if ($Action -eq 'Script') { 'sql' } else { 'xml' }
        $output = Join-Path $repo ".local/database/HoneyDrunkIdentity.$extension"
        New-Item -ItemType Directory -Path (Split-Path $output -Parent) -Force | Out-Null
        $arguments += "/OutputPath:$output"
    }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Database $Action failed." }
} finally { Pop-Location }
