using GoCardlessToYnabSync.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.AddTransient<GoCardlessSyncService>();
        services.AddTransient<YnabSyncService>();
        services.AddSingleton<CosmosDbService>();
        services.AddSingleton<MailService>();
    })
    .ConfigureAppConfiguration((context, builder) =>
    {
        builder.SetBasePath(context.HostingEnvironment.ContentRootPath)
               .AddJsonFile("appsettings.json", false, false)
               .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", false, true)
               .AddUserSecrets<Program>()
               .AddEnvironmentVariables();
    }).Build();


host.Run();