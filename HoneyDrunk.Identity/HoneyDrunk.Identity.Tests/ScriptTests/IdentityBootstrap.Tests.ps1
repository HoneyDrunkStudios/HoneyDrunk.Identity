Describe 'First-initialization SQL execution boundary' {
    It 'rejects competing DDL, rolls back partial work and commits the exact generated schema' {
        & "$PSScriptRoot/../../../scripts/Test-IdentityBootstrap.ps1"
    }
}

Describe 'Database firewall boundary' {
    BeforeDiscovery {
        Import-Module "$PSScriptRoot/../../../scripts/IdentityBootstrap.psm1" -Force
    }
    InModuleScope IdentityBootstrap {
        BeforeEach {
            $script:firewallRules = 0
            $script:tx = [pscustomobject]@{ Committed = $false; RolledBack = $false; Disposed = $false }
            $script:tx | Add-Member ScriptMethod Commit { $this.Committed = $true }
            $script:tx | Add-Member ScriptMethod Rollback { $this.RolledBack = $true }
            $script:tx | Add-Member ScriptMethod Dispose { $this.Disposed = $true }
            $script:connection = [pscustomobject]@{ Transaction = $script:tx }
            $script:connection | Add-Member ScriptMethod BeginTransaction { $this.Transaction }
            Mock Invoke-BootstrapSql {
                if ($Sql -eq "SELECT SERVERPROPERTY('EngineEdition')") { return 5 }
                if ($Sql -eq 'SELECT DB_NAME()') { return 'test-db' }
                if ($Sql -eq 'SELECT @@TRANCOUNT') { return 1 }
                if ($Sql -eq 'SELECT COUNT(*) FROM sys.database_firewall_rules') { return $script:firewallRules }
                if ($Sql -eq 'raced firewall') { $script:firewallRules = 1 }
                return 0
            }
        }
        It 'rejects existing Azure database firewall access before schema inspection' {
            $script:firewallRules = 1
            { Assert-EmptyBootstrapTarget $script:connection 'test-db' '0x01' } | Should -Throw '*database-level firewall rule*'
        }
        It 'rejects a firewall rule that appears after a deployment batch and rolls back' {
            { Invoke-ExactBootstrap $script:connection @('raced firewall', 'must not execute') } | Should -Throw '*database-level firewall rule*'
            $script:tx.Committed | Should -BeFalse
            $script:tx.RolledBack | Should -BeTrue
            $script:tx.Disposed | Should -BeTrue
            Should -Invoke Invoke-BootstrapSql -Times 0 -Exactly -ParameterFilter { $Sql -eq 'must not execute' }
        }
        It 'rejects firewall drift before the first deployment batch' {
            $script:firewallRules = 1
            { Invoke-ExactBootstrap $script:connection @('must not execute') } | Should -Throw '*database-level firewall rule*'
            $script:tx.Committed | Should -BeFalse
            $script:tx.RolledBack | Should -BeTrue
            Should -Invoke Invoke-BootstrapSql -Times 0 -Exactly -ParameterFilter { $Sql -eq 'must not execute' }
        }
        It 'fails closed on an unreadable Azure firewall catalog' {
            Mock Invoke-BootstrapSql { throw 'Catalog access failed' } -ParameterFilter { $Sql -eq 'SELECT COUNT(*) FROM sys.database_firewall_rules' }
            { Invoke-ExactBootstrap $script:connection @('must not execute') } | Should -Throw '*Catalog access failed*'
            $script:tx.RolledBack | Should -BeTrue
        }
        It 'commits an Azure batch only after transactional firewall checks' {
            Invoke-ExactBootstrap $script:connection @('safe batch')
            $script:tx.Committed | Should -BeTrue
            $script:tx.RolledBack | Should -BeFalse
            Should -Invoke Invoke-BootstrapSql -Times 2 -Exactly -ParameterFilter {
                $Sql -eq 'SELECT COUNT(*) FROM sys.database_firewall_rules' -and $Transaction -eq $script:tx
            }
        }
        It 'does not query the Azure-only catalog on LocalDB' {
            Mock Invoke-BootstrapSql { 3 } -ParameterFilter { $Sql -eq "SELECT SERVERPROPERTY('EngineEdition')" }
            Assert-BootstrapFirewall $script:connection
            Should -Invoke Invoke-BootstrapSql -Times 0 -Exactly -ParameterFilter { $Sql -eq 'SELECT COUNT(*) FROM sys.database_firewall_rules' }
        }
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
        & $initializer -Package $package -ApprovedOwnerSid '0x01' -ReviewDirectory $reviewDirectory -SourceRevision $revision
        Should -Invoke Invoke-ExactBootstrap -Times 0 -Exactly
        & $initializer -Action Initialize -Package $package -ApprovedOwnerSid '0x01' -ReviewDirectory $reviewDirectory -SourceRevision $revision
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
        @{ Field = 'database' }, @{ Field = 'revision' }, @{ Field = 'approvedOwnerSid' }
    ) {
        & $initializer -Package $package -ApprovedOwnerSid '0x01' -ReviewDirectory $reviewDirectory -SourceRevision $revision
        $path = Join-Path $reviewDirectory 'manifest.json'
        $manifest = Get-Content $path -Raw | ConvertFrom-Json
        $manifest.$Field = 'changed'
        $manifest | ConvertTo-Json | Set-Content $path
        { & $initializer -Action Initialize -Package $package -ApprovedOwnerSid '0x01' -ReviewDirectory $reviewDirectory -SourceRevision $revision } |
            Should -Throw '*Reviewed source, target or executor changed*'
        Should -Invoke Enter-BootstrapGuard -Times 0 -Exactly
        Should -Invoke Invoke-ExactBootstrap -Times 0 -Exactly
    }
    It 'rejects fresh schema drift and releases maintenance without executing' {
        & $initializer -Package $package -ApprovedOwnerSid '0x01' -ReviewDirectory $reviewDirectory -SourceRevision $revision
        Mock Get-BootstrapReview { @{ Script = 'different SQL'; Report = '<report />' } }
        { & $initializer -Action Initialize -Package $package -ApprovedOwnerSid '0x01' -ReviewDirectory $reviewDirectory -SourceRevision $revision } |
            Should -Throw '*Schema drift*'
        Should -Invoke Invoke-ExactBootstrap -Times 0 -Exactly
        Should -Invoke Exit-BootstrapGuard -Times 1 -Exactly
    }
    It 'rechecks emptiness under the guard and refuses a raced permission' {
        & $initializer -Package $package -ApprovedOwnerSid '0x01' -ReviewDirectory $reviewDirectory -SourceRevision $revision
        Mock Assert-EmptyBootstrapTarget { throw 'Raced permission' } -ParameterFilter { $UnderGuard }
        { & $initializer -Action Initialize -Package $package -ApprovedOwnerSid '0x01' -ReviewDirectory $reviewDirectory -SourceRevision $revision } |
            Should -Throw '*Raced permission*'
        Should -Invoke Exit-BootstrapGuard -Times 1 -Exactly
        Should -Invoke Invoke-ExactBootstrap -Times 0 -Exactly
    }
    It 'rejects an active app before connecting to SQL' {
        Mock az { $global:LASTEXITCODE = 0; '{"enabled":true,"state":"Running"}' }
        { & $initializer -Package $package -ApprovedOwnerSid '0x01' -ReviewDirectory $reviewDirectory -SourceRevision $revision } |
            Should -Throw '*disabled app*'
        Should -Invoke Open-BootstrapConnection -Times 0 -Exactly
    }
    It 'refuses an already initialized database before planning' {
        Mock Assert-EmptyBootstrapTarget { throw 'Nonempty target' }
        { & $initializer -Package $package -ApprovedOwnerSid '0x01' -ReviewDirectory $reviewDirectory -SourceRevision $revision } |
            Should -Throw '*Nonempty target*'
        Should -Invoke Get-BootstrapReview -Times 0 -Exactly
    }
    It 'does not record a successful execution when execution fails' {
        & $initializer -Package $package -ApprovedOwnerSid '0x01' -ReviewDirectory $reviewDirectory -SourceRevision $revision
        Mock Invoke-ExactBootstrap { throw 'Injected execution failure' }
        { & $initializer -Action Initialize -Package $package -ApprovedOwnerSid '0x01' -ReviewDirectory $reviewDirectory -SourceRevision $revision } |
            Should -Throw '*Injected execution failure*'
        Should -Invoke Exit-BootstrapGuard -Times 1 -Exactly
        Test-Path (Join-Path $reviewDirectory 'execution.json') | Should -BeFalse
    }
}
