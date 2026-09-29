using Azure.Core;

namespace HoneyDrunk.Identity.Tests.Fixtures;

internal sealed class GraphTestCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("isolated-test-credential", DateTimeOffset.UtcNow.AddMinutes(1));

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}
