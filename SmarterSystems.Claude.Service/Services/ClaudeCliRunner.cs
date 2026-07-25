using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SmarterSystems.Claude.Service.Services
{
    public sealed class ClaudeCliRequest
    {
        public string Prompt { get; set; }
        public string Model { get; set; }
        public string SessionId { get; set; }
        public string JsonSchema { get; set; }
        public IReadOnlyList<string> AllowedTools { get; set; }
    }

    public sealed class ClaudeCliResult
    {
        public string Text { get; set; }
        public string StopReason { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public decimal CostUsd { get; set; }
    }

    public interface IClaudeCliRunner
    {
        string WorkingDirectory { get; }
        Task<ClaudeCliResult> RunAsync(ClaudeCliRequest request, CancellationToken cancellationToken);
    }

    public sealed class ClaudeCliRunner : IClaudeCliRunner
    {
        private const string ApiBehaviourPrompt =
            "You are answering as an API endpoint. Respond with the answer only, no preamble, no commentary, no status reports about your own configuration.";

        private const string SessionMissingMarker = "No conversation found";
        private const string SessionExistsMarker = "already in use";

        private readonly IConfiguration _configuration;
        private readonly ILogger<ClaudeCliRunner> _logger;

        public ClaudeCliRunner(IConfiguration configuration, ILogger<ClaudeCliRunner> logger, IHostEnvironment environment)
        {
            _configuration = configuration;
            _logger = logger;

            var configured = configuration["ClaudeCli:WorkingDirectory"];
            WorkingDirectory = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(environment.ContentRootPath, "agent-workspace")
                : configured;

            Directory.CreateDirectory(WorkingDirectory);
        }

        public string WorkingDirectory { get; }

        private string ExecutablePath =>
            string.IsNullOrWhiteSpace(_configuration["ClaudeCli:ExecutablePath"])
                ? "claude"
                : _configuration["ClaudeCli:ExecutablePath"];

        private int TimeoutSeconds =>
            int.TryParse(_configuration["ClaudeCli:TimeoutSeconds"], out var seconds) && seconds > 0
                ? seconds
                : 600;

        public async Task<ClaudeCliResult> RunAsync(ClaudeCliRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.SessionId))
                return Parse(await ExecuteAsync(request, SessionMode.None, cancellationToken), request);

            var resumed = await TryExecuteAsync(request, SessionMode.Resume, SessionMissingMarker, cancellationToken);
            if (resumed != null)
                return Parse(resumed, request);

            var created = await TryExecuteAsync(request, SessionMode.Create, SessionExistsMarker, cancellationToken);
            if (created != null)
                return Parse(created, request);

            return Parse(await ExecuteAsync(request, SessionMode.Resume, cancellationToken), request);
        }

        private async Task<string> TryExecuteAsync(
            ClaudeCliRequest request,
            SessionMode mode,
            string recoverableMarker,
            CancellationToken cancellationToken)
        {
            try
            {
                return await ExecuteAsync(request, mode, cancellationToken);
            }
            catch (ClaudeCliException ex) when (ex.Output.Contains(recoverableMarker, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Session {SessionId}: {Mode} not applicable, switching strategy.", request.SessionId, mode);
                return null;
            }
        }

        private async Task<string> ExecuteAsync(ClaudeCliRequest request, SessionMode mode, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ExecutablePath,
                WorkingDirectory = WorkingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.Environment.Remove("ANTHROPIC_API_KEY");
            startInfo.Environment.Remove("ANTHROPIC_AUTH_TOKEN");

            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add(request.Prompt);
            startInfo.ArgumentList.Add("--output-format");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("--model");
            startInfo.ArgumentList.Add(request.Model);
            startInfo.ArgumentList.Add("--append-system-prompt");
            startInfo.ArgumentList.Add(ApiBehaviourPrompt);
            startInfo.ArgumentList.Add("--permission-mode");
            startInfo.ArgumentList.Add("dontAsk");

            if (mode != SessionMode.None)
            {
                startInfo.ArgumentList.Add(mode == SessionMode.Resume ? "--resume" : "--session-id");
                startInfo.ArgumentList.Add(request.SessionId);
            }

            if (!string.IsNullOrWhiteSpace(request.JsonSchema))
            {
                startInfo.ArgumentList.Add("--json-schema");
                startInfo.ArgumentList.Add(request.JsonSchema);
            }

            if (request.AllowedTools != null && request.AllowedTools.Count > 0)
            {
                startInfo.ArgumentList.Add("--allowed-tools");
                startInfo.ArgumentList.Add(string.Join(",", request.AllowedTools));
            }

            using var process = new Process { StartInfo = startInfo };
            var standardOutput = new StringBuilder();
            var standardError = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) standardOutput.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) standardError.AppendLine(e.Data); };

            if (!process.Start())
                throw new InvalidOperationException($"Could not start '{ExecutablePath}'.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw new TimeoutException($"Claude CLI did not finish within {TimeoutSeconds} seconds.");
            }

            if (process.ExitCode != 0)
                throw new ClaudeCliException(process.ExitCode, standardError.ToString(), standardOutput.ToString());

            return standardOutput.ToString();
        }

        private static void TryKill(Process process)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }

        private static ClaudeCliResult Parse(string output, ClaudeCliRequest request)
        {
            var payload = output.Trim();
            if (payload.Length == 0)
                throw new InvalidOperationException("Claude CLI returned no output.");

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True)
            {
                var message = root.TryGetProperty("result", out var errorResult) ? errorResult.GetString() : "unknown error";
                throw new InvalidOperationException($"Claude CLI reported an error: {message}");
            }

            var result = new ClaudeCliResult();
            var expectStructured = !string.IsNullOrWhiteSpace(request.JsonSchema);

            if (expectStructured && root.TryGetProperty("structured_output", out var structured))
                result.Text = structured.GetRawText();
            else if (root.TryGetProperty("result", out var text))
                result.Text = text.GetString();

            if (root.TryGetProperty("stop_reason", out var stopReason) && stopReason.ValueKind == JsonValueKind.String)
                result.StopReason = stopReason.GetString();

            if (root.TryGetProperty("total_cost_usd", out var cost) && cost.ValueKind == JsonValueKind.Number)
                result.CostUsd = cost.GetDecimal();

            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("input_tokens", out var inputTokens) && inputTokens.ValueKind == JsonValueKind.Number)
                    result.InputTokens = inputTokens.GetInt32();
                if (usage.TryGetProperty("output_tokens", out var outputTokens) && outputTokens.ValueKind == JsonValueKind.Number)
                    result.OutputTokens = outputTokens.GetInt32();
            }

            return result;
        }

        private enum SessionMode
        {
            None,
            Create,
            Resume
        }

        private sealed class ClaudeCliException : Exception
        {
            public ClaudeCliException(int exitCode, string standardError, string standardOutput)
                : base(BuildMessage(exitCode, standardError, standardOutput))
            {
                Output = standardError + standardOutput;
            }

            public string Output { get; }

            private static string BuildMessage(int exitCode, string standardError, string standardOutput)
            {
                var detail = standardError.Trim();
                if (detail.Length == 0) detail = standardOutput.Trim();
                return $"Claude CLI exited with code {exitCode}. {detail}";
            }
        }
    }
}
