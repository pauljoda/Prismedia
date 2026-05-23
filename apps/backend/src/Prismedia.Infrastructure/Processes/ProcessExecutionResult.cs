namespace Prismedia.Infrastructure.Processes;

public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
