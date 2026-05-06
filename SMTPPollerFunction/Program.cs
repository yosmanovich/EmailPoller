using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SMTPPoller.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Configure SMTP settings from app settings
        services.Configure<SmtpSettings>(options =>
        {
            var config = context.Configuration;
            options.Host = config["SmtpHost"] ?? string.Empty;
            options.Port = int.TryParse(config["SmtpPort"], out var port) ? port : 25;
            options.EnableSsl = bool.TryParse(config["SmtpEnableSsl"], out var ssl) && ssl;
            options.Username = config["SmtpUsername"];
            options.Password = config["SmtpPassword"];
            options.DefaultFromAddress = config["SmtpDefaultFromAddress"];
            options.TimeoutMs = int.TryParse(config["SmtpTimeoutMs"], out var timeout) ? timeout : 30000;
        });

        // Register services
        services.AddSingleton<ISmtpThrottleService, SmtpThrottleService>();
        services.AddSingleton<IEmailService, EmailService>();
        services.AddSingleton<IEmailQueueRepository, EmailQueueRepository>();
    })
    .Build();

host.Run();
