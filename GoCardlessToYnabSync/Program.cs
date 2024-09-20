using GoCardlessToYnabSync.Options;
using GoCardlessToYnabSync.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Configuration;


var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((hostContext, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.AddTransient<GoCardlessSyncService>();
        services.AddTransient<YnabSyncService>();
        services.AddTransient<CosmosDbService>();
        services.AddTransient<MailService>();


        services.Configure<GoCardlessOptions>(hostContext.Configuration.GetSection(GoCardlessOptions.GoCardless));
        services.Configure<YnabOptions>(hostContext.Configuration.GetSection(YnabOptions.Ynab));
        services.Configure<FunctionUriOptions>(hostContext.Configuration.GetSection(FunctionUriOptions.FunctionUris));
        services.Configure<CosmosDbOptions>(hostContext.Configuration.GetSection(CosmosDbOptions.CosmosDb));
        services.Configure<SmptOptions>(hostContext.Configuration.GetSection(SmptOptions.Smpt));


        //services.AddOptions<FunctionUriOptions>().Configure<IConfiguration>((settings, configuration) => {
        //    hostContext.Configuration.GetSection(FunctionUriOptions.FunctionUris);
        //});
        //services.AddOptions<GoCardlessOptions>().Configure<IConfiguration>((settings, configuration) => {
        //    hostContext.Configuration.GetSection(GoCardlessOptions.GoCardless);
        //});
        //services.AddOptions<SmptOptions>().Configure<IConfiguration>((settings, configuration) => {
        //    hostContext.Configuration.GetSection(SmptOptions.Smpt);
        //});
        //services.AddOptions<YnabOptions>().Configure<IConfiguration>((settings, configuration) => {
        //    hostContext.Configuration.GetSection(YnabOptions.Ynab);
        //});
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