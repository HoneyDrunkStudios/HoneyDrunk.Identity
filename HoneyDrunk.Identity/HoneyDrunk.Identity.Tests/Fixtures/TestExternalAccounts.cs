using HoneyDrunk.Identity.Abstractions.Authentication;

namespace HoneyDrunk.Identity.Tests.Fixtures;

internal sealed class TestExternalAccounts : IExternalAccounts
{
    public HashSet<string> Erased { get; } = [];

    public bool Fail { get; set; }

    public Task<bool> Exists(ExternalSubject subject, CancellationToken token) => Task.FromResult(!Erased.Contains(subject.Subject));

    public Task Revoke(ExternalSubject subject, CancellationToken token) => Fail ? Task.FromException(new HttpRequestException("Test provider unavailable.")) : Task.CompletedTask;

    public Task Erase(ExternalSubject subject, CancellationToken token)
    {
        if (Fail)
            throw new HttpRequestException("Test provider unavailable.");
        Erased.Add(subject.Subject);
        return Task.CompletedTask;
    }
}
