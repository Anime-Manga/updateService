using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MassTransit;
using System;
using Cesxhin.AnimeManga.Modules.Generic;
using NLog;
using Cesxhin.AnimeManga.Application.CheckManager.Interfaces;
using Cesxhin.AnimeManga.Application.CheckManager;
using Cesxhin.AnimeManga.Modules.Schema;

namespace Cesxhin.AnimeManga.UpdateService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            SchemaControl.Check();

            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    //rabbit
                    services.AddMassTransit(
                    x =>
                    {
                        x.UsingRabbitMq((context, cfg) =>
                        {
                            cfg.Host(
                                Environment.GetEnvironmentVariable("ADDRESS_RABBIT") ?? "localhost",
                                "/",
                                credentials =>
                                {
                                    credentials.Username(Environment.GetEnvironmentVariable("USERNAME_RABBIT") ?? "guest");
                                    credentials.Password(Environment.GetEnvironmentVariable("PASSWORD_RABBIT") ?? "guest");
                                });
                        });
                    });

                    //setup nlog
                    var level = Environment.GetEnvironmentVariable("LOG_LEVEL").ToLower() ?? "info";
                    LogLevel logLevel = NLogManager.GetLevel(level);
                    NLogManager.Configure(logLevel);

                    //select service between anime or manga
                    var serviceSelect = Environment.GetEnvironmentVariable("SELECT_SERVICE") ?? "video";

                    if(serviceSelect.ToLower().Contains("video"))
                        services.AddTransient<IUpdate, UpdateVideo>();
                    else
                        services.AddTransient<IUpdate, UpdateBook>();

                    services.AddHostedService<Worker>();
                });
    }
}
