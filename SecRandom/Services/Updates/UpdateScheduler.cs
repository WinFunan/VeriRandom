using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Services.Config;

namespace SecRandom.Services.Updates;

public sealed class UpdateScheduler(
    MainConfigHandler configHandler,
    UpdateCenterService updateCenter,
    IUpdateNotificationService updateNotificationService,
    ILogger<UpdateScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!GlobalConstants.UpdatesEnabled)
        {
            // Keep the hosted service registered so re-enabling updates is a one-line policy change.
            logger.LogInformation("此分支已禁用自动更新，跳过更新调度。");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = configHandler.Data.UpdateSettings;
                if (settings.AutoUpdateMode > 0
                    && (!settings.LastCheckTime.HasValue || DateTime.Now - settings.LastCheckTime.Value >= TimeSpan.FromDays(1)))
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await updateCenter.CheckAsync();
                        if (!updateCenter.CanDownloadAndInstall)
                            return;
                        if (settings.AutoUpdateMode == 1)
                            await updateNotificationService.ShowUpdateAvailableAsync(updateCenter.AvailableVersion);
                        else if (settings.AutoUpdateMode == 2)
                            await updateCenter.DownloadAsync(installAfterDownload: false);
                        else
                        {
                            await updateCenter.DownloadAsync(installAfterDownload: false);
                            await updateCenter.ApplyDownloadedUpdateAsync();
                        }
                    }, DispatcherPriority.Normal).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "自动检查更新失败。");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
