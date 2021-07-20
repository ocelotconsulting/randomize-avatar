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
        /// <param name="user_id">The user that is assigned this home tab</param>
        /// <param name="team_id">The team ID associated with this workspace</param>
        public static async Task UpdateHomeTab(string user_id, string team_id)
        {
            // Check for the parameters
            if (string.IsNullOrEmpty(user_id))
                throw new ArgumentNullException(paramName: nameof(user_id));
                
            if (string.IsNullOrEmpty(team_id))
                throw new ArgumentNullException(paramName: nameof(team_id));

            // Get the token for this team
            var workspaceBot = TableHandler.GetWorkspaceBot(team_id);

            if (workspaceBot == default(WorkspaceBotEntity))
                throw new ArgumentException($"Unable to lookup the bot entry for team '{team_id}'");

            // Get our user's information so we know their current settings
            var user = TableHandler.GetUser(user_id, team_id);

            // We need to get the file contents we will be sending
            var jsonBody = await File.ReadAllTextAsync(Path.Join(ClientInteractivity.ViewsDirectory, ClientInteractivity.HomeTab));

            // Replace the user_id setting
            jsonBody = jsonBody.Replace("{USER_ID}", user_id);

            using(var client = new HttpClient())
            {
                // Set our Bearer header
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", workspaceBot.accessToken);

                // Create our body
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                // Send the content
                await client.PostAsync("https://slack.com/api/views.publish", content);
            }
        }
    }
}
#nullable restore
