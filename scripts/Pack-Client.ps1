[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$identityRoot = Split-Path $PSScriptRoot -Parent
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
foreach ($project in @('HoneyDrunk.Identity.Abstractions', 'HoneyDrunk.Identity.Client')) {
    dotnet pack (Join-Path $identityRoot "HoneyDrunk.Identity/$project/$project.csproj") --configuration Release --output $outputPath --nologo
    if ($LASTEXITCODE -ne 0) { throw "Packaging $project failed." }
}
Write-Host 'Created local alpha package candidates. Nothing was published.'
