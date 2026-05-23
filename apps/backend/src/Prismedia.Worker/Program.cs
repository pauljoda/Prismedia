using Prismedia.Application;
using Prismedia.Infrastructure;
using Prismedia.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddPrismediaApplication();
builder.Services.AddPrismediaWorkerApplication();
builder.Services.AddPrismediaInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

var host = builder.Build();
await PrismediaMigrationRunner.ApplyPrismediaMigrationsAsync(host.Services, builder.Configuration);
host.Run();
