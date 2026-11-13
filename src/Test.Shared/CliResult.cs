namespace Test.Shared
{
    internal sealed class CliResult
    {
        internal int ExitCode { get; init; }
        internal string Output { get; init; } = "";
        internal string Error { get; init; } = "";
    }
}
