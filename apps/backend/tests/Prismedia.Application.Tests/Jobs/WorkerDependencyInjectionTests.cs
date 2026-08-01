using Microsoft.Extensions.DependencyInjection;
using Prismedia.Application.Jobs;
namespace Prismedia.Application.Tests.Jobs;

public sealed class WorkerDependencyInjectionTests {
    [Fact]
    public void WorkerCompositionRegistersEveryDiscoveredHandlerByConcreteType() {
        var services = new ServiceCollection();

        services.AddPrismediaWorkerApplication();

        foreach (var handlerType in JobDefinitionRegistry.All.Select(definition => definition.HandlerType).Distinct()) {
            Assert.Contains(services, descriptor =>
                descriptor.ServiceType == handlerType &&
                descriptor.ImplementationType == handlerType &&
                descriptor.Lifetime == ServiceLifetime.Transient);
        }
    }
}
