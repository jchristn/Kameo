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
            if (Array.IndexOf(args, "--version") >= 0)
            {
                string home = Environment.GetEnvironmentVariable("KAMEO_HOME") ?? throw new InvalidOperationException("The fixture requires an isolated home.");
                string path = Path.Combine(home, "probe-version.txt");
                string version = File.Exists(path) ? File.ReadAllText(path) : "1.5.7";
                if (version == "hang") await Task.Delay(30000, CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine(version);
                return version == "fail" ? 1 : 0;
            }
            int capture = Array.IndexOf(args, "--capture");
            if (capture < 0 || capture + 1 == args.Length) return 2;
            JsonArray arguments = new JsonArray();
            foreach (string argument in args) arguments.Add((JsonNode?)JsonValue.Create(argument));
            JsonObject environment = new JsonObject();
            foreach (string name in new[] { "KAMEO_PROFILE_ID", "CODEX_HOME", "CLAUDE_CONFIG_DIR", "GEMINI_CLI_HOME", "MUX_CONFIG_DIR", "ANTHROPIC_API_KEY", "OPENAI_API_KEY", "GEMINI_API_KEY", "KAMEO_MUX_API_KEY", "SSH_CONNECTION", "AGY_CLI_DISABLE_AUTO_UPDATE" })
                environment[name] = Environment.GetEnvironmentVariable(name);
            JsonObject result = new JsonObject { ["arguments"] = arguments, ["environment"] = environment, ["cwd"] = Environment.CurrentDirectory };
            string capturePath = args[capture + 1];
            await File.WriteAllTextAsync(capturePath + ".tmp", result.ToJsonString(), CancellationToken.None).ConfigureAwait(false);
            File.Move(capturePath + ".tmp", capturePath);
            int token = Array.IndexOf(args, "--fixture-token");
            if (token >= 0)
            {
                int directory = Array.IndexOf(args, "--gemini_dir");
                string path = Path.Combine(args[directory + 1], "antigravity-cli", "antigravity-oauth-token");
                JsonObject document = new JsonObject { ["auth_method"] = "consumer", ["token"] = new JsonObject { ["access_token"] = args[token + 1], ["refresh_token"] = "synthetic-refresh" } };
                await File.WriteAllTextAsync(path, document.ToJsonString(), CancellationToken.None).ConfigureAwait(false);
            }
            int sleep = Array.IndexOf(args, "--sleep");
            if (sleep >= 0) await Task.Delay(30000, CancellationToken.None).ConfigureAwait(false);
            int wait = Array.IndexOf(args, "--wait-for-file");
            if (wait >= 0)
            {
                using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (!File.Exists(args[wait + 1])) await Task.Delay(25, timeout.Token).ConfigureAwait(false);
            }
            int exit = Array.IndexOf(args, "--exit");
            return exit >= 0 ? int.Parse(args[exit + 1], System.Globalization.CultureInfo.InvariantCulture) : 0;
        }
    }
}
