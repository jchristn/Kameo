namespace Test.Xunit3
{
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Xunit3Adapter;
    using Xunit;

    /// <summary>Executes each shared descriptor as an independently reported xUnit test.</summary>
    public sealed class KameoTests
    {
        /// <summary>All shared test cases.</summary>
        public static TheoryData<TouchstoneXunit3Case> Cases() => TouchstoneXunit3Cases.From<KameoSuites>();
        /// <summary>Executes a single shared case.</summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public Task Run(TouchstoneXunit3Case testCase) => TouchstoneXunit3.ExecuteAsync(testCase, TestContext.Current.CancellationToken);
    }
}
