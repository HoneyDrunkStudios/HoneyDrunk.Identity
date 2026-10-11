Describe 'Offline database review boundary' {
    It 'rejects drift and tampering while keeping SQL publication disabled' {
        & "$PSScriptRoot/../../../scripts/Test-DatabaseReview.ps1"
    }
}
