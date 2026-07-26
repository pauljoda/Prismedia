using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Jobs.Handlers.Maintenance;

namespace Prismedia.Application.Tests.Jobs;

public sealed class WorkerDependencyInjectionTests {
    [Fact]
    public void WorkerCompositionRegistersTheSharedEntityProcessingPlanner() {
        var services = new ServiceCollection();

        services.AddPrismediaWorkerApplication();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(EntityProcessingGraphPlanner) &&
            descriptor.ImplementationType == typeof(EntityProcessingGraphPlanner) &&
            descriptor.Lifetime == ServiceLifetime.Transient);
    }
}
