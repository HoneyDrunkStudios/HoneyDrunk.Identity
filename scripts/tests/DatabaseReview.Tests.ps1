Describe 'Offline database review boundary' {
    It 'rejects drift and tampering while keeping SQL publication disabled' {
        & "$PSScriptRoot/../Test-DatabaseReview.ps1"
    }
}
