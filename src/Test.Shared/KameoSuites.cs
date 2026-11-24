namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>Every core contract, integration and process case, shared by all test runners including native AOT.</summary>
    public sealed class KameoSuites : ITestSuiteProvider
    {
        /// <summary>Fresh descriptors for the complete suite.</summary>
        public static IReadOnlyList<TestSuiteDescriptor> All => new[] { StorageCases.Build(), CredentialCases.Build(), LaunchCases.Build(), LifecycleCases.Build(), CliCases.Build(), MuxEndpointCases.Build(), AntigravityCases.Build(), UsageCases.Build(), PublisherCases.Build() };
        /// <inheritdoc />
        public IReadOnlyList<TestSuiteDescriptor> Suites => All;

        internal static TestCaseDescriptor Case(string suite, string name, Action<Fixture> execute) => Case(suite, name, (fixture, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested(); execute(fixture); return Task.CompletedTask;
        });
        internal static TestCaseDescriptor Case(string suite, string name, Func<Fixture, CancellationToken, Task> execute) => new TestCaseDescriptor(suite, name, name,
            async cancellationToken => { using Fixture fixture = new Fixture(); await execute(fixture, cancellationToken).ConfigureAwait(false); }, tags: new[] { "isolated" });
    }
}
