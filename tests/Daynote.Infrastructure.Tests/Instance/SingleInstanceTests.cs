using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Daynote.Infrastructure.Instance;

namespace Daynote.Infrastructure.Tests.Instance;

[TestClass]
public sealed class SingleInstanceTests
{
    /// <summary>Shared registry that deterministically simulates the OS mutex + activation pipe in-process.</summary>
    private sealed class InstanceRegistry
    {
        private int _claimed;

        public int ActivationCount;

        public bool TryClaim() => Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;

        public event Action? Activation;

        public void Signal()
        {
            Interlocked.Increment(ref ActivationCount);
            Activation?.Invoke();
        }
    }

    private sealed class FakeClaim(InstanceRegistry registry) : IPrimaryClaim
    {
        public bool TryClaim() => registry.TryClaim();

        public void Dispose()
        {
        }
    }

    private sealed class FakeChannel(InstanceRegistry registry) : IActivationChannel
    {
        public void StartListening(Action onActivation) => registry.Activation += onActivation;

        public Task<bool> SignalAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            registry.Signal();
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [TestMethod]
    public async Task Test_Start_when_twenty_launches_race_yields_exactly_one_primary_and_resolves_all_secondaries()
    {
        var registry = new InstanceRegistry();
        var coordinators = Enumerable.Range(0, 20)
            .Select(_ => new SingleInstanceCoordinator(new FakeClaim(registry), new FakeChannel(registry)))
            .ToArray();

        var primaryActivations = new ConcurrentBag<int>();
        int primaryIndex = -1;

        var roles = new SingleInstanceRole[coordinators.Length];
        await Task.WhenAll(coordinators.Select((coordinator, index) => Task.Run(() =>
        {
            SingleInstanceRole role = coordinator.Start();
            roles[index] = role;
            if (role == SingleInstanceRole.Primary)
            {
                Interlocked.Exchange(ref primaryIndex, index);
                coordinator.ActivationRequested += (_, _) => primaryActivations.Add(1);
            }
        })));

        Assert.AreEqual(1, roles.Count(r => r == SingleInstanceRole.Primary), "Exactly one primary must win.");
        Assert.AreEqual(19, roles.Count(r => r == SingleInstanceRole.Secondary));

        int secondaries = 0;
        foreach ((SingleInstanceCoordinator coordinator, int index) in coordinators.Select((c, i) => (c, i)))
        {
            if (roles[index] == SingleInstanceRole.Secondary)
            {
                Assert.IsTrue(await coordinator.ActivatePrimaryAsync(TimeSpan.FromSeconds(1)));
                secondaries++;
            }
        }

        Assert.AreEqual(19, secondaries);
        Assert.AreEqual(19, registry.ActivationCount);
        Assert.AreEqual(19, primaryActivations.Count, "Every secondary activation must reach the primary.");

        foreach (SingleInstanceCoordinator coordinator in coordinators)
        {
            await coordinator.DisposeAsync();
        }
    }

    [TestMethod]
    [TestCategory("WindowsSmoke")]
    public void Test_MutexPrimaryClaim_grants_only_one_owner_and_releases_on_dispose()
    {
        // Named-mutex ownership is per-thread, so the holder must be a distinct, long-lived thread with
        // explicit synchronization (a background Task can be inlined/reused, which makes this flaky).
        string name = $@"Local\Daynote-test-{Guid.NewGuid():N}";
        using var acquired = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        MutexPrimaryClaim? holder = null;
        var ownerThread = new Thread(() =>
        {
            holder = new MutexPrimaryClaim(name);
            Assert.IsTrue(holder.TryClaim(), "First claim must win.");
            acquired.Set();
            release.Wait();
            holder.Dispose();
        })
        {
            IsBackground = true,
        };
        ownerThread.Start();
        Assert.IsTrue(acquired.Wait(TimeSpan.FromSeconds(5)), "The owner thread must acquire the mutex.");

        // While the owner holds it, a second claim on this thread must fail.
        var second = new MutexPrimaryClaim(name);
        Assert.IsFalse(second.TryClaim(), "A second concurrent claim must fail while the first owns it.");
        second.Dispose();

        // After the owner releases, a new launch may claim it.
        release.Set();
        Assert.IsTrue(ownerThread.Join(TimeSpan.FromSeconds(5)), "The owner thread must release and exit.");

        var third = new MutexPrimaryClaim(name);
        Assert.IsTrue(third.TryClaim(), "After the primary releases, a new launch may claim it.");
        third.Dispose();
    }

    [TestMethod]
    [TestCategory("WindowsSmoke")]
    public void Test_CurrentUserPipeSecurity_restricts_access_to_the_current_user_only()
    {
        PipeSecurity security = CurrentUserPipeSecurity.Create();
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier currentUser = identity.User!;

        AuthorizationRuleCollection rules = security.GetAccessRules(
            includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));

        Assert.AreEqual(1, rules.Count, "Only the current user may be granted access.");
        var rule = (PipeAccessRule)rules[0]!;
        Assert.AreEqual(currentUser, rule.IdentityReference);
        Assert.AreEqual(AccessControlType.Allow, rule.AccessControlType);
        Assert.IsTrue(rule.PipeAccessRights.HasFlag(PipeAccessRights.FullControl));
    }

    [TestMethod]
    [TestCategory("WindowsSmoke")]
    public async Task Test_NamedPipeActivationChannel_delivers_a_secondary_signal_to_the_primary()
    {
        string pipeName = $"Daynote-test-{Guid.NewGuid():N}";
        await using var primary = new NamedPipeActivationChannel(pipeName);
        using var signaled = new SemaphoreSlim(0, 1);
        primary.StartListening(() => signaled.Release());

        await using var secondary = new NamedPipeActivationChannel(pipeName);
        Assert.IsTrue(await secondary.SignalAsync(TimeSpan.FromSeconds(2), CancellationToken.None));

        Assert.IsTrue(await signaled.WaitAsync(TimeSpan.FromSeconds(2)), "The primary must receive the activation.");
    }
}
