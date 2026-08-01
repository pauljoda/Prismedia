using System.Collections.Immutable;
using System.Reflection;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Jobs;

/// <summary>
/// Discovers durable job definitions from <see cref="JobDefinitionAttribute"/> declarations in the
/// application assembly. Startup fails when any domain job type is missing or declared more than once.
/// </summary>
public static class JobDefinitionRegistry {
    private static readonly ImmutableDictionary<JobType, JobDefinition> DefinitionsByType =
        BuildForHandlerTypes(typeof(JobDefinitionRegistry).Assembly.GetTypes());

    /// <summary>Gets every discovered job definition ordered by durable job type.</summary>
    public static IReadOnlyList<JobDefinition> All { get; } =
        DefinitionsByType.Values.OrderBy(definition => definition.Type).ToArray();

    /// <summary>Gets the definition for a durable job type.</summary>
    public static JobDefinition Get(JobType type) =>
        DefinitionsByType.TryGetValue(type, out var definition)
            ? definition
            : throw new InvalidOperationException($"No discovered job definition exists for '{type.ToCode()}'.");

    /// <summary>Returns the job type's default CPU cost class.</summary>
    public static JobResourceClass ResourceClass(JobType type) => Get(type).ResourceClass;

    /// <summary>Returns whether failure should block required graph completion.</summary>
    public static JobNodeImportance Importance(JobType type) => Get(type).Importance;

    /// <summary>Returns whether the job is queue-wide singleton work for the supplied target scope.</summary>
    public static bool IsQueueWideSingleton(JobType type, bool hasTarget) =>
        Get(type).SingletonBehavior switch {
            JobSingletonBehavior.QueueWide => true,
            JobSingletonBehavior.QueueWideWhenUntargeted => !hasTarget,
            _ => false
        };

    /// <summary>Returns whether outstanding jobs of this type delay background Auto Identify.</summary>
    public static bool BlocksAutoIdentify(JobType type) => Get(type).BlocksAutoIdentify;

    internal static ImmutableDictionary<JobType, JobDefinition> BuildForHandlerTypes(IEnumerable<Type> types) {
        var handlerTypes = types
            .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(IJobHandler).IsAssignableFrom(type))
            .ToArray();
        var undeclaredHandlers = handlerTypes
            .Where(type => !type.GetCustomAttributes<JobDefinitionAttribute>(inherit: false).Any())
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (undeclaredHandlers.Length > 0) {
            throw new InvalidOperationException(
                $"Job handler discovery found handlers without definitions: {string.Join(", ", undeclaredHandlers)}.");
        }

        var definitions = handlerTypes
            .SelectMany(handlerType => handlerType
                .GetCustomAttributes<JobDefinitionAttribute>(inherit: false)
                .Select(attribute => new JobDefinition(
                    attribute.Type,
                    handlerType,
                    attribute.ResourceClass,
                    attribute.Importance,
                    attribute.SingletonBehavior,
                    attribute.BlocksAutoIdentify)))
            .ToArray();

        var duplicates = definitions
            .GroupBy(definition => definition.Type)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key.ToCode())
            .OrderBy(code => code)
            .ToArray();
        if (duplicates.Length > 0) {
            throw new InvalidOperationException(
                $"Job handler discovery found duplicate definitions for: {string.Join(", ", duplicates)}.");
        }

        var definitionsByType = definitions.ToImmutableDictionary(definition => definition.Type);
        var missing = Enum.GetValues<JobType>()
            .Where(type => !definitionsByType.ContainsKey(type))
            .Select(type => type.ToCode())
            .OrderBy(code => code)
            .ToArray();
        if (missing.Length > 0) {
            throw new InvalidOperationException(
                $"Job handler discovery is missing definitions for: {string.Join(", ", missing)}.");
        }

        return definitionsByType;
    }
}

/// <summary>Immutable dispatch and scheduling policy for one durable job type.</summary>
public sealed record JobDefinition(
    JobType Type,
    Type HandlerType,
    JobResourceClass ResourceClass,
    JobNodeImportance Importance,
    JobSingletonBehavior SingletonBehavior,
    bool BlocksAutoIdentify);
