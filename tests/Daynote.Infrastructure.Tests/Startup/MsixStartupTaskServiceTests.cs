using Daynote.Core.Startup;
using Daynote.Infrastructure.Startup;

namespace Daynote.Infrastructure.Tests.Startup;

[TestClass]
public sealed class MsixStartupTaskServiceTests
{
    /// <summary>Records enable/disable calls so we can prove the service never retries a refused enable.</summary>
    private sealed class FakeGateway(StartupTaskState state) : IStartupTaskGateway
    {
        public StartupTaskState State { get; set; } = state;

        public int EnableCalls { get; private set; }

        public int DisableCalls { get; private set; }

        public ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(State);

        public ValueTask<StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken)
        {
            EnableCalls++;
            State = StartupTaskState.Enabled;
            return ValueTask.FromResult(State);
        }

        public ValueTask<StartupTaskState> DisableAsync(CancellationToken cancellationToken)
        {
            DisableCalls++;
            State = StartupTaskState.Disabled;
            return ValueTask.FromResult(State);
        }
    }

    [TestMethod]
    public async Task Test_RequestEnable_when_disabled_enables_and_reports_change()
    {
        var gateway = new FakeGateway(StartupTaskState.Disabled);
        var service = new MsixStartupTaskService(gateway);

        StartupEnableResult result = await service.RequestEnableAsync();

        Assert.AreEqual(StartupTaskState.Enabled, result.State);
        Assert.IsTrue(result.Changed);
        Assert.IsTrue(result.IsEnabled);
        Assert.AreEqual(1, gateway.EnableCalls);
    }

    [TestMethod]
    public async Task Test_RequestEnable_when_disabled_by_user_reports_state_without_retrying_enable()
    {
        var gateway = new FakeGateway(StartupTaskState.DisabledByUser);
        var service = new MsixStartupTaskService(gateway);

        StartupEnableResult result = await service.RequestEnableAsync();

        Assert.AreEqual(StartupTaskState.DisabledByUser, result.State);
        Assert.IsFalse(result.Changed);
        Assert.IsTrue(result.IsUserOrPolicyControlled);
        Assert.AreEqual(0, gateway.EnableCalls, "A user-disabled task must never be re-enabled from the app.");
    }

    [TestMethod]
    public async Task Test_RequestEnable_when_disabled_by_policy_reports_state_without_retrying_enable()
    {
        var gateway = new FakeGateway(StartupTaskState.DisabledByPolicy);
        var service = new MsixStartupTaskService(gateway);

        StartupEnableResult result = await service.RequestEnableAsync();

        Assert.AreEqual(StartupTaskState.DisabledByPolicy, result.State);
        Assert.IsFalse(result.Changed);
        Assert.AreEqual(0, gateway.EnableCalls);
    }

    [TestMethod]
    public async Task Test_RequestEnable_when_unavailable_reports_clean_state_without_enabling()
    {
        var gateway = new FakeGateway(StartupTaskState.Unavailable);
        var service = new MsixStartupTaskService(gateway);

        StartupEnableResult result = await service.RequestEnableAsync();

        Assert.AreEqual(StartupTaskState.Unavailable, result.State);
        Assert.IsFalse(result.Changed);
        Assert.IsFalse(result.IsEnabled);
        Assert.AreEqual(0, gateway.EnableCalls);
    }

    [TestMethod]
    public async Task Test_GetState_defaults_to_the_gateway_reported_state()
    {
        var gateway = new FakeGateway(StartupTaskState.Disabled);
        var service = new MsixStartupTaskService(gateway);

        Assert.AreEqual(StartupTaskState.Disabled, await service.GetStateAsync());
    }

    [TestMethod]
    public async Task Test_RequestDisable_when_enabled_disables_but_leaves_policy_states_unchanged()
    {
        var enabled = new MsixStartupTaskService(new FakeGateway(StartupTaskState.Enabled));
        StartupEnableResult disabled = await enabled.RequestDisableAsync();
        Assert.AreEqual(StartupTaskState.Disabled, disabled.State);
        Assert.IsTrue(disabled.Changed);

        var policyGateway = new FakeGateway(StartupTaskState.EnabledByPolicy);
        var policyService = new MsixStartupTaskService(policyGateway);
        StartupEnableResult policy = await policyService.RequestDisableAsync();
        Assert.AreEqual(StartupTaskState.EnabledByPolicy, policy.State);
        Assert.IsFalse(policy.Changed);
        Assert.AreEqual(0, policyGateway.DisableCalls);
    }

    [TestMethod]
    public async Task Test_WindowsStartupTaskGateway_reports_unavailable_when_unpackaged()
    {
        // The test host has no packaged identity, so the real gateway must degrade to Unavailable
        // rather than throwing.
        var gateway = new WindowsStartupTaskGateway("DaynoteStartupTask");
        Assert.AreEqual(StartupTaskState.Unavailable, await gateway.GetStateAsync(CancellationToken.None));
    }
}
