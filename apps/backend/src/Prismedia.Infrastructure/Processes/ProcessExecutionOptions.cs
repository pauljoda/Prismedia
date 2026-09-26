namespace Prismedia.Infrastructure.Processes;

/// <summary>Output budgets and environment inheritance for one captured child process.</summary>
/// <param name="MaxStandardOutputCharacters">Maximum decoded stdout characters; null leaves capture unbounded.</param>
/// <param name="MaxStandardErrorCharacters">Maximum decoded stderr characters; null leaves capture unbounded.</param>
/// <param name="InheritEnvironment">Whether the child receives the parent's environment before explicit overrides.</param>
public sealed record ProcessExecutionOptions(
    int? MaxStandardOutputCharacters = null,
    int? MaxStandardErrorCharacters = null,
    bool InheritEnvironment = true);

/// <summary>The child exceeded its capture budget and was terminated before its output could exhaust host memory.</summary>
public sealed class ProcessOutputLimitException() : IOException("Process output exceeded the configured capture limit.");
