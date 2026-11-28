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
            if (Array.IndexOf(args, "app-server") >= 0) return await AppServerAsync(args, CancellationToken.None).ConfigureAwait(false);
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
            foreach (string name in new[] { "CLAUDE_SECURESTORAGE_CONFIG_DIR", "ANTHROPIC_CONFIG_DIR" })
                environment[name] = Environment.GetEnvironmentVariable(name);
            bool hasClaudeOverride = false;
            foreach (string name in new[] { "ANTHROPIC_PROFILE", "ANTHROPIC_FEDERATION_RULE_ID", "ANTHROPIC_ORGANIZATION_ID", "ANTHROPIC_IDENTITY_TOKEN", "ANTHROPIC_IDENTITY_TOKEN_FILE", "CLAUDE_CODE_FEDERATION_CACHE_DIR" })
                hasClaudeOverride |= Environment.GetEnvironmentVariable(name) != null;
            JsonObject result = new JsonObject { ["arguments"] = arguments, ["environment"] = environment, ["cwd"] = Environment.CurrentDirectory };
            result["hasClaudeCredentialOverride"] = hasClaudeOverride;
            result["pid"] = Environment.ProcessId;
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
            int refreshed = Array.IndexOf(args, "--write-credential");
            if (refreshed >= 0) await WriteCredentialAsync(args, args[refreshed + 1], CancellationToken.None).ConfigureAwait(false);
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

        // Acts like a harness refreshing its native login: writes a synthetic credential where the harness keeps it.
        private static async Task WriteCredentialAsync(string[] args, string token, CancellationToken cancellationToken)
        {
            string path;
            JsonObject document;
            int gemini = Array.IndexOf(args, "--gemini_dir");
            if (Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is string claude)
            {
                path = Path.Combine(claude, ".credentials.json");
                document = new JsonObject { ["claudeAiOauth"] = new JsonObject { ["accessToken"] = token, ["refreshToken"] = token + "-refresh", ["expiresAt"] = 1900000000000 } };
            }
            else if (Environment.GetEnvironmentVariable("CODEX_HOME") is string codex)
            {
                path = Path.Combine(codex, "auth.json");
                document = new JsonObject { ["tokens"] = new JsonObject { ["access_token"] = token, ["refresh_token"] = token + "-refresh" } };
            }
            else if (Environment.GetEnvironmentVariable("MUX_CONFIG_DIR") is string mux)
            {
                path = Path.Combine(mux, "endpoints.json");
                document = new JsonObject { ["endpoints"] = new JsonArray(new JsonObject { ["name"] = "test", ["adapterType"] = "openai", ["model"] = token }) };
            }
            else if (gemini >= 0)
            {
                path = Path.Combine(args[gemini + 1], "antigravity-cli", "antigravity-oauth-token");
                document = new JsonObject { ["auth_method"] = "consumer", ["token"] = new JsonObject { ["access_token"] = token, ["refresh_token"] = token + "-refresh" } };
            }
            else throw new InvalidOperationException("The fixture could not find a harness credential location.");
            await File.WriteAllTextAsync(path + ".tmp", document.ToJsonString(), cancellationToken).ConfigureAwait(false);
            File.Move(path + ".tmp", path, true);
        }

        // A synthetic Codex app-server. CODEX_HOME/fixture-mode.txt selects "ok", "error", "hang" or "exit";
        // CODEX_HOME/fixture-rate-limits.json supplies the rate-limit result. Records requests before answering usage.
        private static async Task<int> AppServerAsync(string[] args, CancellationToken cancellationToken)
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? throw new InvalidOperationException("The fixture requires CODEX_HOME.");
            string modePath = Path.Combine(home, "fixture-mode.txt");
            string mode = File.Exists(modePath) ? File.ReadAllText(modePath).Trim() : "ok";
            JsonArray arguments = new JsonArray();
            foreach (string argument in args) arguments.Add((JsonNode?)JsonValue.Create(argument));
            JsonArray methods = new JsonArray();
            string? line;
            while ((line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
            {
                JsonNode? message = JsonNode.Parse(line);
                string method = message?["method"]?.GetValue<string>() ?? "";
                methods.Add((JsonNode?)JsonValue.Create(method));
                if (method == "initialize")
                {
                    if (mode == "exit") return 1;
                    Console.Out.WriteLine("{\"method\":\"configWarning\",\"params\":{\"summary\":\"synthetic\"}}");
                    Console.Out.WriteLine("{\"id\":1,\"result\":{\"userAgent\":\"synthetic\",\"codexHome\":" + JsonValue.Create(home).ToJsonString() + "}}");
                    Console.Out.Flush();
                }
                else if (method == "account/rateLimits/read")
                {
                    JsonObject capture = new JsonObject
                    {
                        ["arguments"] = arguments, ["methods"] = methods, ["cwd"] = Environment.CurrentDirectory, ["codexHome"] = home,
                        ["params"] = message?["params"]?.DeepClone(), ["openAiKey"] = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    };
                    await File.WriteAllTextAsync(Path.Combine(home, "app-server-capture.json"), capture.ToJsonString(), CancellationToken.None).ConfigureAwait(false);
                    if (mode == "hang") { await Task.Delay(30000, CancellationToken.None).ConfigureAwait(false); return 0; }
                    Console.Out.WriteLine("{\"method\":\"account/rateLimits/updated\",\"params\":{}}");
                    if (mode == "error") Console.Out.WriteLine("{\"id\":2,\"error\":{\"code\":-32600,\"message\":\"codex account authentication required to read rate limits\"}}");
                    else Console.Out.WriteLine("{\"id\":2,\"result\":" + File.ReadAllText(Path.Combine(home, "fixture-rate-limits.json")) + "}");
                    Console.Out.Flush();
                }
            }
            return 0;
        }
    }
}
