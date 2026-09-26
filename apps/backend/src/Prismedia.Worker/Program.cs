using Prismedia.Application;
using Prismedia.Application.Security;
using Prismedia.Infrastructure;
using Prismedia.Infrastructure.Database;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Security;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddPrismediaApplication();
builder.Services.AddPrismediaWorkerApplication();
builder.Services.AddPrismediaInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

// Background jobs run as the system context: unrestricted library visibility, no user,
// and therefore no per-user engagement reads or writes.
builder.Services.AddScoped<ICurrentUserContext>(_ => {
    var context = new CurrentUserContextHolder();
    context.SetSystem();
    return context;
});

var host = builder.Build();

// The API owns destructive restore startup and schema application. The worker waits for any staged
// restore to finish, then waits for the database to be reachable
// and migrated so it neither races the API to migrate a fresh database nor terminates when the
// database is not yet accepting connections on first boot.
await DatabaseRestoreRunner.WaitForPendingRestoreToClearAsync(host.Services, builder.Configuration);
await PrismediaMigrationRunner.WaitForDatabaseReadyAsync(host.Services, builder.Configuration);
await ProviderCredentialUpgradeRunner.UpgradeAsync(host.Services, builder.Configuration);
host.Run();
