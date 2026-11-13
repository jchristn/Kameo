namespace Test.Aot
{
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Cli;

    internal static class Program
    {
        private static Task<int> Main(string[] args) => ConsoleRunner.RunMainAsync(KameoSuites.All, args);
    }
}
