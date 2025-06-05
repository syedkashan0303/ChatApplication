using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SignalRMVC.CustomClasses
{
    public class AppWatchdogService : BackgroundService
    {
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly ILogger<AppWatchdogService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _baseUrl;
        private readonly IServiceScopeFactory _scopeFactory;


        public AppWatchdogService(IHostApplicationLifetime appLifetime, ILogger<AppWatchdogService> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration, IServiceScopeFactory scopeFactory)
        {
            _appLifetime = appLifetime;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _baseUrl = configuration["AppSettings:BaseUrl"]?.TrimEnd('/')
                ?? throw new ArgumentNullException("BaseUrl not configured in appsettings.json");
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            //var client = _httpClientFactory.CreateClient();

            //while (!stoppingToken.IsCancellationRequested)
            //{
            //    try
            //    {
            //        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)); // Wait max 1 minute
            //        var response = await client.GetAsync(_baseUrl+"/Home/ping", cts.Token);

            //        if (!response.IsSuccessStatusCode)
            //        {
            //            _logger.LogError("Health check failed with status: " + response.StatusCode);

            //            await NotifyUsersAndShutdownAsync();
            //            break;
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        _logger.LogError(ex, "App seems unresponsive. Triggering restart.");
            //        await NotifyUsersAndShutdownAsync();
            //        break;
            //    }

            //    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // check every minute
            //}
        }

        private async Task NotifyUsersAndShutdownAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<BasicChatHub>>();
                await hubContext.Clients.All.SendAsync("RedirectToLogin");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify users via SignalR before shutdown.");
            }

            _appLifetime.StopApplication();
        }

    }
}
