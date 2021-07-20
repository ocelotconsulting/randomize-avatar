using System;
using System.Linq;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

// Collections
using System.Collections.Generic;

// MemoryStream
using System.IO;

// HTTP Requests
using System.Web;
using System.Net;
using Microsoft.Azure.Functions.Worker.Http;

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
        /// The file for our frequency options tab
        /// </summary>
        public const string FrequencyOptions = "FrequencyOptions.json";

        /// <summary>
        /// The frequency options we provide to our users
        /// </summary>
        /// <typeparam name="int">The number of seconds between each update</typeparam>
        /// <typeparam name="string">A user-friendly string</typeparam>
        public static Dictionary<int, string> FrequencyOptionsDict = new Dictionary<int, string>()
        {
            { 3600, "Every Hour" },
            { 7200, "Every 2 Hours" },
            { 14400, "Every 4 Hours" },
            { 28800, "Every 8 Hours" },
            { 43200, "Every 12 Hours" },
            { 86400, "Every Day" },
            { 172800, "Every 2 Days" },
            { 604800, "Every Week" }
        };

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

            // Empty user, nothing to do
            if (user == default(UserEntity))
                return;

            // We need to get the file contents we will be sending
            var jsonBody = await File.ReadAllTextAsync(Path.Join(ClientInteractivity.ViewsDirectory, ClientInteractivity.HomeTab));

            // Replace the user_id setting
            jsonBody = jsonBody.Replace("{USER_ID}", user_id);

            // Get all of our options
            var options = new Dictionary<int, string>();
            foreach(var kvp in ClientInteractivity.FrequencyOptionsDict.OrderBy(a => a.Key))
            {
                var tempBody = await GetFrequencyOption(kvp.Key);

                if (!string.IsNullOrEmpty(tempBody))
                    options.Add(kvp.Key, tempBody);
            }

            // Replace our option list
            jsonBody = jsonBody.Replace("{OPTION_LIST}", string.Join(", ", options.Select(a => a.Value)));

            // Replace our initial option
            if (options.ContainsKey(user.UpdateFrequencySeconds))
            {
                // Replace our initial option
                jsonBody = jsonBody.Replace("{INITIAL_OPTION}", options[user.UpdateFrequencySeconds]);
            }
            else
            {
                // Replace our initial option with a blank object
                jsonBody = jsonBody.Replace("{INITIAL_OPTION}", "{}");
            }

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

        /// <summary>
        /// A method to return the JSON string that describes an option value
        /// </summary>
        /// <param name="value">A value in seconds that corresponds to <see cref="OcelotConsulting.Avatars.ClientInteractivity.FrequencyOptionsDict"/></param>
        /// <returns>A JSON string</returns>
        public static async Task<string> GetFrequencyOption(int value)
        {
            // We need to get the file contents we will be sending
            var jsonBody = await File.ReadAllTextAsync(Path.Join(ClientInteractivity.ViewsDirectory, ClientInteractivity.FrequencyOptions));

            // Get the option
            string option = string.Empty;
            try
            {
                option = ClientInteractivity.FrequencyOptionsDict[value];
            }
            catch
            {
                return string.Empty;
            }

            // Replace the value
            jsonBody = jsonBody.Replace("{OPTION_VALUE}", value.ToString("D"));

            // Replace the user-friendly string
            jsonBody = jsonBody.Replace("{OPTION_TEXT}", option);

            return jsonBody;
        }

        /// <summary>
        /// This is the HTTP endpoint that Slack will send POST requests to. The app MUST respond with 200 OK within 3 seconds.
        /// https://api.slack.com/interactivity/handling#acknowledgment_response
        /// </summary>
        [Function("SlackInteractiveResponse")]
        public static async Task<HttpResponseData> SlackInteractiveResponse([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req,
            FunctionContext executionContext)
        {
            // Our app will only accept incoming block actions
            // Due to the design of the app, this is the only request that matters
            // https://api.slack.com/reference/interaction-payloads/block-actions
            string payloadString;

            // Read the body
            using (var sr = new StreamReader(req.Body))
            {
                payloadString = (await sr.ReadToEndAsync()).Trim();
            }
            
            // Check for a good response
            if (string.IsNullOrEmpty(payloadString))
                return req.CreateResponse(HttpStatusCode.InternalServerError);

            // Clear our with a good response
            return req.CreateResponse(HttpStatusCode.OK);
        }
    }

    /// <summary>
    /// This is the expected format of a block_action payload
    /// </summary>
    public class BlockActionPayload
    {
        public string type { get; set; } = string.Empty;
        public OAuthV2AuthorizeTeam? team { get; set; } = null;
        public OAuthV2AuthorizeAuthedUser? user { get; set; } = null;

        public string api_app_id { get; set; } = string.Empty;
        public string token { get; set; } = string.Empty;
        public string trigger_id { get; set; } = string.Empty;

        // The actual block actions
        public List<Action> actions { get; set; } = new List<Action>();
    }

    /// <summary>
    /// This is the expected format of a block_action item
    /// </summary>
    public class Action
    {
        public string action_id { get; set; } = string.Empty;
        public string block_id { get; set; } = string.Empty;
        public string value { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public string action_ts { get; set; } = string.Empty;
    }
}
#nullable restore
