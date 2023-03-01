using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Slack.Webhooks;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace CheckSslCertificateExpiration
{
    public static class CheckSslCertificateExpiration
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        [FunctionName("CheckSslCertificateExpiration")]
        public static async Task RunAsync(
            [TimerTrigger("0 0 0 * * 0", 
#if DEBUG
                RunOnStartup= true
#else
                RunOnStartup= false
#endif
            )] TimerInfo timer,
            ExecutionContext context)
        {
            var config = new ConfigurationBuilder()
#if DEBUG
                .SetBasePath(context.FunctionAppDirectory+"\\..\\..\\..\\")
#else
                .SetBasePath(context.FunctionAppDirectory)
#endif
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var websites = config.GetSection("Websites").Get<List<string>>();
            var daysBeforeExpiration = config.GetValue<int>("DaysBeforeExpiration");
            var slackWebhookUrl = config.GetValue<string>("SlackWebhookUrl");

            foreach (var websiteUrl in websites)
            {
                var httpClientHandler = new HttpClientHandler();
                httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                {
                    var expirationDate = cert?.GetExpirationDateString();
                    var daysUntilExpiration = (DateTime.Parse(expirationDate) - DateTime.UtcNow).TotalDays;
                    if (daysUntilExpiration <= daysBeforeExpiration)
                    {
                        var slackClient = new SlackClient(slackWebhookUrl);
                        var slackMessage = new SlackMessage
                        {
                            Channel = "#devops",
                            Text = $"The SSL certificate for {websiteUrl} will expire in {daysUntilExpiration:F0} days ({expirationDate})"
                        };
                        slackClient.Post(slackMessage);
                    }
                    return true;
                };

                using (var httpClient = new HttpClient(httpClientHandler))
                {
                    await httpClient.GetAsync(websiteUrl);
                }
            }
        }
    }
}
