[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$packagePath = (Resolve-Path $PackageDirectory).Path
$packages = @(Get-ChildItem -LiteralPath $packagePath -Filter '*.nupkg')
if ($packages.Count -ne 2) { throw 'Expected only Client and Abstractions packages.' }
$client = @($packages | Where-Object Name -Match '^HoneyDrunk\.Identity\.Client\.(\d.*)\.nupkg$')
if ($client.Count -ne 1) { throw 'Expected exactly one Client package.' }
$version = [regex]::Match($client[0].Name, '^HoneyDrunk\.Identity\.Client\.(\d.*)\.nupkg$').Groups[1].Value
if (-not (Test-Path (Join-Path $packagePath "HoneyDrunk.Identity.Abstractions.$version.nupkg"))) { throw 'Client and Abstractions versions must match.' }
$consumerPath = Join-Path ([IO.Path]::GetTempPath()) ('identity-consumer-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $consumerPath | Out-Null
$escapedSource = [System.Security.SecurityElement]::Escape($packagePath)
$config = "<configuration><packageSources><clear/><add key=`"candidates`" value=`"$escapedSource`"/><add key=`"nuget.org`" value=`"https://api.nuget.org/v3/index.json`"/></packageSources><packageSourceMapping><packageSource key=`"candidates`"><package pattern=`"HoneyDrunk.Identity.*`"/></packageSource><packageSource key=`"nuget.org`"><package pattern=`"*`"/></packageSource></packageSourceMapping></configuration>"
Set-Content -LiteralPath (Join-Path $consumerPath 'NuGet.Config') -Value $config
Set-Content -LiteralPath (Join-Path $consumerPath 'Consumer.csproj') -Value "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup><ItemGroup><PackageReference Include=`"HoneyDrunk.Identity.Client`" Version=`"[$version]`"/></ItemGroup></Project>"
Set-Content -LiteralPath (Join-Path $consumerPath 'Consumer.cs') -Value 'public sealed class Consumer { public HoneyDrunk.Identity.Client.IdentityClient Create(System.Net.Http.HttpClient http) => new(http); public HoneyDrunk.Identity.Abstractions.UserRecord Echo(HoneyDrunk.Identity.Abstractions.UserRecord user) => user; }'
dotnet restore (Join-Path $consumerPath 'Consumer.csproj') --packages (Join-Path $consumerPath 'cache')
if ($LASTEXITCODE -ne 0) { throw 'Isolated consumer restore failed.' }
dotnet restore (Join-Path $consumerPath 'Consumer.csproj') --locked-mode --packages (Join-Path $consumerPath 'cache')
if ($LASTEXITCODE -ne 0) { throw 'Locked consumer restore failed.' }
dotnet build (Join-Path $consumerPath 'Consumer.csproj') --no-restore --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Isolated consumer build failed.' }
Write-Host "Verified package-only consumer in $consumerPath. Nothing was published."
