namespace Test.Nunit
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Test.Shared;
    using Touchstone.Core;
    using Touchstone.NunitAdapter;

    /// <summary>Executes the shared Touchstone suites through NUnit.</summary>
    [TestFixture]
    public sealed class KameoTests : TouchstoneNunitFactBase
    {
        /// <inheritdoc />
        protected override IReadOnlyList<TestSuiteDescriptor> Suites => KameoSuites.All;
        /// <summary>Runs the full set of isolated shared cases.</summary>
        [Test]
        public Task RunAll() => RunAllAsync(TestContext.CurrentContext.CancellationToken);
    }
}
