Describe 'Isolated package consumer contract' {
    BeforeAll {
        $script:packageVerifier = Join-Path $PSScriptRoot '../../../scripts/Test-Packages.ps1'
        function dotnet { }
    }

    AfterAll {
        Remove-Variable -Name IdentityPackageTestcalls, IdentityPackageTestconsumerDirectory, IdentityPackageTestfailureAt -Scope Global -ErrorAction SilentlyContinue
    }

    BeforeEach {
        $global:IdentityPackageTestcalls = [Collections.Generic.List[object]]::new()
        $global:IdentityPackageTestconsumerDirectory = $null
        $global:IdentityPackageTestfailureAt = 0
        $script:packages = Join-Path $TestDrive 'candidates & packages'
        New-Item -ItemType Directory -Force -Path $script:packages | Out-Null
        foreach ($name in @('Client', 'Abstractions')) {
            Set-Content -LiteralPath (Join-Path $script:packages "HoneyDrunk.Identity.$name.0.1.0-alpha.5.nupkg") -Value 'Offline fixture'
        }
        Mock dotnet {
            $global:IdentityPackageTestcalls.Add([string[]]$args)
            $global:IdentityPackageTestconsumerDirectory = Split-Path $args[1] -Parent
            $global:LASTEXITCODE = if ($global:IdentityPackageTestcalls.Count -eq $global:IdentityPackageTestfailureAt) { 1 } else { 0 }
        }
    }

    AfterEach {
        if ($global:IdentityPackageTestconsumerDirectory -and (Test-Path -LiteralPath $global:IdentityPackageTestconsumerDirectory)) {
            Remove-Item -LiteralPath $global:IdentityPackageTestconsumerDirectory -Recurse -Force
        }
    }

    It 'generates both supported targets with isolated candidate mapping and locked verification' {
        & $script:packageVerifier -PackageDirectory $script:packages
        $project = [xml](Get-Content -LiteralPath (Join-Path $global:IdentityPackageTestconsumerDirectory 'Consumer.csproj') -Raw)
        $project.Project.PropertyGroup.TargetFrameworks | Should -BeExactly 'net10.0;net11.0'
        $project.Project.PropertyGroup.RestorePackagesWithLockFile | Should -BeExactly 'true'
        $project.Project.ItemGroup.PackageReference.Version | Should -BeExactly '[0.1.0-alpha.5]'
        $config = [xml](Get-Content -LiteralPath (Join-Path $global:IdentityPackageTestconsumerDirectory 'NuGet.Config') -Raw)
        $config.configuration.packageSources.add[0].value | Should -BeExactly $script:packages
        $config.configuration.packageSourceMapping.packageSource[0].package.pattern | Should -BeExactly 'HoneyDrunk.Identity.*'
        $global:IdentityPackageTestcalls.Count | Should -Be 3
        $global:IdentityPackageTestcalls[0][0] | Should -BeExactly 'restore'
        $global:IdentityPackageTestcalls[0] | Should -Contain '--packages'
        $global:IdentityPackageTestcalls[1] | Should -Contain '--locked-mode'
        $global:IdentityPackageTestcalls[2][0] | Should -BeExactly 'build'
        $global:IdentityPackageTestcalls[2] | Should -Contain '--no-restore'
    }

    It 'stops on failed <Stage> without accepting incomplete verification' -TestCases @(
        @{ Stage = 'initial restore'; FailureAt = 1; Message = 'Isolated consumer restore failed.' }
        @{ Stage = 'locked restore'; FailureAt = 2; Message = 'Locked consumer restore failed.' }
        @{ Stage = 'build'; FailureAt = 3; Message = 'Isolated consumer build failed.' }
    ) {
        param($FailureAt, $Message)
        $global:IdentityPackageTestfailureAt = $FailureAt
        { & $script:packageVerifier -PackageDirectory $script:packages } | Should -Throw $Message
        $global:IdentityPackageTestcalls.Count | Should -Be $FailureAt
    }
}
