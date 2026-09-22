/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Notifications.Discord
{
    /// <summary>
    /// Starts the bundled Discord bot at application startup when the integration
    /// is enabled and a bot token is configured. Without this the bot only ran
    /// after a manual Start Bot click and stayed down after every restart.
    /// </summary>
    public class DiscordBotAutoStartService(
        IServiceScopeFactory scopeFactory,
        IDiscordBotService discordBotService,
        IHostApplicationLifetime lifetime,
        ILogger<DiscordBotAutoStartService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // The bot talks back to Listenarr over HTTP, so wait until the web host
            // is accepting requests before launching it.
            var applicationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var startedRegistration = lifetime.ApplicationStarted.Register(() => applicationStarted.TrySetResult());
            using var stoppingRegistration = stoppingToken.Register(() => applicationStarted.TrySetCanceled(stoppingToken));

            try
            {
                await applicationStarted.Task;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                bool enabled;
                bool hasToken;
                using (var scope = scopeFactory.CreateScope())
                {
                    var configurationService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
                    var settings = await configurationService.GetApplicationSettingsAsync();
                    enabled = settings.DiscordBotEnabled;
                    hasToken = !string.IsNullOrWhiteSpace(settings.DiscordBotToken);
                }

                if (!enabled)
                {
                    logger.LogDebug("Discord bot integration is disabled; not starting the bot automatically");
                    return;
                }

                if (!hasToken)
                {
                    logger.LogInformation("Discord bot integration is enabled but no bot token is configured; not starting the bot automatically");
                    return;
                }

                logger.LogInformation("Discord bot integration is enabled; starting the bot automatically");
                var started = await discordBotService.StartBotAsync();
                if (!started)
                {
                    logger.LogWarning("Discord bot did not start automatically; start it from Settings > Discord Bot");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("Discord bot auto start canceled due to host shutdown");
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(ex, "Discord bot auto start failed");
            }
        }
    }
}
