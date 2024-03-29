using Cesxhin.AnimeManga.Application.CheckManager.Interfaces;
using Cesxhin.AnimeManga.Modules.NlogManager;
using Microsoft.Extensions.Hosting;
using NLog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cesxhin.AnimeManga.UpdateService
{
    public class Worker : BackgroundService
    {
        //log
        private readonly NLogConsole _logger = new(LogManager.GetCurrentClassLogger());

        //timer
        private readonly int _timeRefresh = int.Parse(Environment.GetEnvironmentVariable("TIME_REFRESH") ?? "120000");

        //service
        private readonly IUpdate _update;

        public Worker(IUpdate update)
        {
            _update = update;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _update.ExecuteUpdate();
                }catch (Exception ex)
                {
                    _logger.Fatal($"Error update, details error: {ex}");
                }

                _logger.Info($"Worker running at: {DateTimeOffset.Now}");
                await Task.Delay(_timeRefresh, stoppingToken);
            }
        }
    }
}
