using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Jobs.Handlers;
using Prismedia.Application.Jobs.Handlers.Scan;
using Prismedia.Application.Requests;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>Declares the acquisition-profile naming family owned by an application strategy.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AcquisitionStrategyAttribute(AcquisitionNamingFamily namingFamily) : Attribute {
    /// <summary>The profile naming family the decorated strategy serves.</summary>
    public AcquisitionNamingFamily NamingFamily { get; } = namingFamily;
}

/// <summary>
/// Discovers acquisition strategies and verifies that every request acquisition kind has one owner in
/// each applicable execution stage. Entity definitions remain the source of the kind-to-family mapping.
/// </summary>
public static class AcquisitionStrategyRegistration {
    /// <summary>
    /// Registers family-declared search policies needed by both the API and worker processes.
    /// Import and materialization strategies remain worker-only because their constructors depend
    /// on scan handlers and transfer services that the API deliberately does not host.
    /// </summary>
    public static void RegisterApplicationStrategies(IServiceCollection services) {
        var strategies = Discover();
        ValidateCoverage(strategies);
        Register<IAcquisitionPolicyModule>(services, strategies, ServiceLifetime.Singleton);
    }

    /// <summary>Registers family-declared import and materialization strategies for the worker process.</summary>
    public static void RegisterWorkerStrategies(IServiceCollection services) {
        var strategies = Discover();
        ValidateCoverage(strategies);
        Register<IAcquisitionImportEngine>(services, strategies, ServiceLifetime.Scoped);
        RegisterImplementations<IImportedEntityMaterializationPolicy>(services, ServiceLifetime.Scoped);
    }

    /// <summary>Validates the discovered application strategies without constructing their dependencies.</summary>
    public static void ValidateCoverage() => ValidateCoverage(Discover());

    /// <summary>Returns the naming family governing an acquisition kind, or null outside the request flow.</summary>
    public static AcquisitionNamingFamily? TryGetNamingFamily(EntityKind kind) =>
        EntityKindRegistry.Describe(AcquisitionProfileKinds.For(kind)).AcquisitionProfile?.NamingFamily;

    /// <summary>Builds an exact, family-derived lookup for every request acquisition kind.</summary>
    public static IReadOnlyDictionary<EntityKind, TStrategy> ResolveByAcquisitionKind<TStrategy>(
        IEnumerable<TStrategy> strategies,
        string strategyName) where TStrategy : class {
        var byFamily = strategies
            .GroupBy(FamilyOf)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var byKind = new Dictionary<EntityKind, TStrategy>();
        foreach (var kind in AcquisitionKinds) {
            var family = TryGetNamingFamily(kind)
                ?? throw new InvalidOperationException(
                    $"Request acquisition kind '{kind.ToCode()}' has no acquisition profile naming family.");
            if (!byFamily.TryGetValue(family, out var matches) || matches.Length == 0) {
                throw new InvalidOperationException(
                    $"No {strategyName} is registered for acquisition kind '{kind.ToCode()}' (family '{family.ToCode()}').");
            }
            if (matches.Length > 1) {
                throw new InvalidOperationException(
                    $"Acquisition kind '{kind.ToCode()}' has multiple {strategyName} registrations for family '{family.ToCode()}': " +
                    string.Join(", ", matches.Select(StrategyName)) + ".");
            }

            byKind.Add(kind, matches[0]);
        }

        return byKind;
    }

    /// <summary>Gets the family declared by a concrete strategy instance.</summary>
    public static AcquisitionNamingFamily FamilyOf(object strategy) =>
        strategy.GetType().GetCustomAttribute<AcquisitionStrategyAttribute>()?.NamingFamily
        ?? throw new InvalidOperationException(
            $"Acquisition strategy '{StrategyName(strategy)}' must declare {nameof(AcquisitionStrategyAttribute)}.");

    private static IReadOnlyList<EntityKind> AcquisitionKinds { get; } = RequestKindRegistry.All
        .SelectMany(descriptor => new[] { descriptor.AcquisitionKind, descriptor.ProfileEntityKind })
        .OfType<EntityKind>()
        .Distinct()
        .OrderBy(kind => kind.ToCode(), StringComparer.Ordinal)
        .ToArray();

    private static IReadOnlyList<Type> Discover() => typeof(DependencyInjection).Assembly
        .GetTypes()
        .Where(type => type is { IsAbstract: false, IsInterface: false }
            && type.GetCustomAttribute<AcquisitionStrategyAttribute>() is not null)
        .ToArray();

    private static void Register<TService>(
        IServiceCollection services,
        IEnumerable<Type> strategies,
        ServiceLifetime lifetime) where TService : class {
        foreach (var strategy in strategies.Where(typeof(TService).IsAssignableFrom)) {
            services.Add(new ServiceDescriptor(typeof(TService), strategy, lifetime));
        }
    }

    private static void RegisterImplementations<TService>(
        IServiceCollection services,
        ServiceLifetime lifetime) where TService : class {
        foreach (var implementation in typeof(DependencyInjection).Assembly.GetTypes().Where(type =>
                     type is { IsAbstract: false, IsInterface: false }
                     && typeof(TService).IsAssignableFrom(type))) {
            services.Add(new ServiceDescriptor(typeof(TService), implementation, lifetime));
        }
    }

    private static void ValidateCoverage(IReadOnlyList<Type> strategies) {
        ValidateCoverage<IAcquisitionPolicyModule>(strategies, "search policy", static _ => true);
        ValidateCoverage<IAcquisitionImportEngine>(strategies, "import engine", static _ => true);
    }

    private static void ValidateCoverage<TService>(
        IReadOnlyList<Type> strategies,
        string strategyName,
        Func<EntityKind, bool> applies) where TService : class {
        var candidates = strategies
            .Where(typeof(TService).IsAssignableFrom)
            .ToArray();
        foreach (var kind in AcquisitionKinds.Where(applies)) {
            var family = TryGetNamingFamily(kind)
                ?? throw new InvalidOperationException(
                    $"Request acquisition kind '{kind.ToCode()}' has no acquisition profile naming family.");
            var matches = candidates
                .Where(type => type.GetCustomAttribute<AcquisitionStrategyAttribute>()!.NamingFamily == family)
                .ToArray();
            if (matches.Length != 1) {
                throw new InvalidOperationException(
                    $"Acquisition kind '{kind.ToCode()}' requires exactly one {strategyName} for family " +
                    $"'{family.ToCode()}', but found {matches.Length}: {string.Join(", ", matches.Select(StrategyName))}.");
            }
        }
    }

    private static string StrategyName(object strategy) => StrategyName(strategy.GetType());

    private static string StrategyName(Type strategy) => strategy.FullName ?? strategy.Name;
}
