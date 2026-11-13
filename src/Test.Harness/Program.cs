namespace Test.Harness
{
    using System;
    using System.IO;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            int capture = Array.IndexOf(args, "--capture");
            if (capture < 0 || capture + 1 == args.Length) return 2;
            JsonArray arguments = new JsonArray();
            foreach (string argument in args) arguments.Add((JsonNode?)JsonValue.Create(argument));
            JsonObject environment = new JsonObject();
            foreach (string name in new[] { "KAMEO_PROFILE_ID", "CODEX_HOME", "CLAUDE_CONFIG_DIR", "GEMINI_CLI_HOME", "MUX_CONFIG_DIR", "ANTHROPIC_API_KEY", "OPENAI_API_KEY", "GEMINI_API_KEY", "KAMEO_MUX_API_KEY" })
                environment[name] = Environment.GetEnvironmentVariable(name);
            JsonObject result = new JsonObject { ["arguments"] = arguments, ["environment"] = environment, ["cwd"] = Environment.CurrentDirectory };
            await File.WriteAllTextAsync(args[capture + 1], result.ToJsonString(), CancellationToken.None).ConfigureAwait(false);
            int sleep = Array.IndexOf(args, "--sleep");
            if (sleep >= 0) await Task.Delay(30000, CancellationToken.None).ConfigureAwait(false);
            int exit = Array.IndexOf(args, "--exit");
            return exit >= 0 ? int.Parse(args[exit + 1], System.Globalization.CultureInfo.InvariantCulture) : 0;
        }
    }
}
