Describe 'First-initialization SQL execution boundary' {
    It 'rejects competing DDL, rolls back partial work and commits the exact generated schema' {
        & "$PSScriptRoot/../../../scripts/Test-IdentityBootstrap.ps1"
    }
}

Describe 'Initializer approval and cleanup boundary' {
    BeforeAll {
        Import-Module "$PSScriptRoot/../../../scripts/IdentityBootstrap.psm1" -Force
        # This harness must never invoke the real Azure CLI.
        function az { throw 'Unmocked Azure CLI call in offline test' }
        $initializer = "$PSScriptRoot/../../../scripts/Initialize-DevDatabase.ps1"
        $revision = 'a' * 40
    }
    BeforeEach {
        $package = Join-Path $TestDrive 'schema.dacpac'
        Set-Content $package 'test package'
        $reviewDirectory = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $fakeConnection = [pscustomobject]@{ State = 'Open'; Disposed = $false }
        $fakeConnection | Add-Member ScriptMethod Dispose { $this.Disposed = $true }
        Mock Import-Module {}
        Mock az {
            $global:LASTEXITCODE = 0
            if ($args[0] -eq 'webapp') { '{"enabled":false,"state":"Stopped"}' }
            else { 'offline-test-token' }
        }
        Mock Open-BootstrapConnection { $fakeConnection }
        Mock Assert-EmptyBootstrapTarget {}
        Mock Get-BootstrapReview { @{ Script = 'reviewed SQL'; Report = '<report />' } }
        Mock ConvertTo-BootstrapBatches { 'reviewed SQL' }
        Mock Enter-BootstrapGuard {}
        Mock Exit-BootstrapGuard {}
        Mock Invoke-ExactBootstrap {}
    }
    It 'plans without executing and commits only the matching fresh review' {
        & $initializer -Package $package -ReviewDirectory $reviewDirectory -SourceRevision $revision
        Should -Invoke Invoke-ExactBootstrap -Times 0 -Exactly
        & $initializer -Action Initialize -Package $package -ReviewDirectory $reviewDirectory -SourceRevision $revision
        Should -Invoke Enter-BootstrapGuard -Times 1 -Exactly
        Should -Invoke Invoke-ExactBootstrap -Times 1 -Exactly
        Should -Invoke Exit-BootstrapGuard -Times 1 -Exactly
        $fakeConnection.Disposed | Should -BeTrue
        Test-Path (Join-Path $reviewDirectory 'execution.json') | Should -BeTrue
    }
    It 'rejects a changed <Field> before entering maintenance' -ForEach @(
        @{ Field = 'wrapperSha256' }, @{ Field = 'executorSha256' },
        @{ Field = 'packageSha256' }, @{ Field = 'scriptSha256' },
        @{ Field = 'reportSha256' }, @{ Field = 'server' },
        @{ Field = 'database' }, @{ Field = 'revision' }
    ) {
        & $initializer -Package $package -ReviewDirectory $reviewDirectory -SourceRevision $revision
        $path = Join-Path $reviewDirectory 'manifest.json'
        $manifest = Get-Content $path -Raw | ConvertFrom-Json
        $manifest.$Field = 'changed'
        $manifest | ConvertTo-Json | Set-Content $path
        { & $initializer -Action Initialize -Package $package -ReviewDirectory $reviewDirectory -SourceRevision $revision } |
            Should -Throw '*Reviewed source, target or executor changed*'
        Should -Invoke Enter-BootstrapGuard -Times 0 -Exactly
        Should -Invoke Invoke-ExactBootstrap -Times 0 -Exactly
    }
    It 'rejects fresh schema drift and releases maintenance without executing' {
        & $initializer -Package $package -ReviewDirectory $reviewDirectory -SourceRevision $revision
        Mock Get-BootstrapReview { @{ Script = 'different SQL'; Report = '<report />' } }
        { & $initializer -Action Initialize -Package $package -ReviewDirectory $reviewDirectory -SourceRevision $revision } |
            Should -Throw '*Schema drift*'
        Should -Invoke Invoke-ExactBootstrap -Times 0 -Exactly
        Should -Invoke Exit-BootstrapGuard -Times 1 -Exactly
    }
    It 'rechecks emptiness under the guard and refuses a raced permission' {
        & $initializer -Package $package -ReviewDirectory $reviewDirectory -SourceRevision $revision
        Mock Assert-EmptyBootstrapTarget { throw 'Raced permission' } -ParameterFilter { $UnderGuard }
        { & $initializer -Action Initialize -Package $package -ReviewDirectory $reviewDirectory -SourceRevision $revision } |
            Should -Throw '*Raced permission*'
        Should -Invoke Exit-BootstrapGuard -Times 1 -Exactly
        Should -Invoke Invoke-ExactBootstrap -Times 0 -Exactly
    }
    It 'rejects an active app before connecting to SQL' {
        Mock az { $global:LASTEXITCODE = 0; '{"enabled":true,"state":"Running"}' }
        { & $initializer -Package $package -ReviewDirectory $reviewDirectory -SourceRevision $revision } |
            Should -Throw '*disabled app*'
        Should -Invoke Open-BootstrapConnection -Times 0 -Exactly
    }
    It 'refuses an already initialized database before planning' {
        Mock Assert-EmptyBootstrapTarget { throw 'Nonempty target' }
        { & $initializer -Package $package -ReviewDirectory $reviewDirectory -SourceRevision $revision } |
            Should -Throw '*Nonempty target*'
        Should -Invoke Get-BootstrapReview -Times 0 -Exactly
    }
    It 'does not record a successful execution when execution fails' {
        & $initializer -Package $package -ReviewDirectory $reviewDirectory -SourceRevision $revision
        Mock Invoke-ExactBootstrap { throw 'Injected execution failure' }
        { & $initializer -Action Initialize -Package $package -ReviewDirectory $reviewDirectory -SourceRevision $revision } |
            Should -Throw '*Injected execution failure*'
        Should -Invoke Exit-BootstrapGuard -Times 1 -Exactly
        Test-Path (Join-Path $reviewDirectory 'execution.json') | Should -BeFalse
    }
}
