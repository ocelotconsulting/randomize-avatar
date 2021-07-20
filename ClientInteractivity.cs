using System;
using System.Linq;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

// MemoryStream
using System.IO;

// JSON Handling
using System.Text.Json;
using System.Text;

// HTTP Client
using System.Net.Http;
using System.Net.Http.Headers;

// Async work
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace OcelotConsulting.Avatars
{
    public static class ClientInteractivity
    {
        /// <summary>
        /// The directory name with all of the JSON content
        /// </summary>
        public const string ViewsDirectory = "SlackViews";

        /// <summary>
        /// The file for our home tab
        /// </summary>
        public const string HomeTab = "HomeTab.json";

        /// <summary>
        /// This is a background job that will update our home tab in a specific workspace with a given <paramref name="botAccessToken"/>
        /// </summary>
        /// <param name="botAccessToken">An access token that must begin with "xoxb-"</param>
        /// <param name="userId">The user that is assigned this home tab</param>
        public static async Task UpdateHomeTab(string botAccessToken, string userId)
        {
            // Check for the parameters
            if (string.IsNullOrEmpty(botAccessToken))
                throw new ArgumentNullException(paramName: nameof(botAccessToken));
                
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentNullException(paramName: nameof(userId));

            // Does it look valid?
            if (!botAccessToken.Trim().StartsWith("xoxb-", StringComparison.InvariantCultureIgnoreCase))
                throw new ArgumentException("Invalid token provided, not a bot access token.", paramName: nameof(botAccessToken));

            // We have a (potentially) valid botAccessToken
            botAccessToken = botAccessToken.Trim();

            // We need to get the file contents we will be sending
            var jsonBody = await File.ReadAllTextAsync(Path.Join(ClientInteractivity.ViewsDirectory, ClientInteractivity.HomeTab));

            // Replace the user_id setting
            jsonBody = jsonBody.Replace("{USER_ID}", userId);

            using(var client = new HttpClient())
            {
                // Set our Bearer header
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", botAccessToken);

                // Create our body
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                // Send the content
                await client.PostAsync("https://slack.com/api/views.publish", content);
            }
        }
    }
}
#nullable restore
