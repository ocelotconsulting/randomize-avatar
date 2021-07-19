using System;
using System.Linq;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

// HTTP Requests
using System.Web;
using System.Net;
using Microsoft.Azure.Functions.Worker.Http;

// MemoryStream
using System.IO;

// Dictionary
using System.Collections.Generic;

// JSON Handling
using System.Text.Json;

// HTTP Client
using System.Net.Http;

// Async work
using System.Threading;
using System.Threading.Tasks;

// Image manipulation
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;

#nullable enable
namespace OcelotConsulting.Avatars
{
    public static class SignInWithSlackFunction
    {
        /// <summary>
        /// The endpoint we are targeting to perform the Slack OAuth redirect
        /// </summary>
        public static string SlackAuthorize = "https://slack.com/oauth/v2/authorize";
        
        /// <summary>
        /// The endpoint we are targeting to perform the Slack OAuth redirect (https://api.slack.com/methods/oauth.v2.access)
        /// </summary>
        public static string SlackAccess = "https://slack.com/api/oauth.v2.access";

        [Function("SignInWithSlack")]
        public static HttpResponseData SignInWithSlack([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req,
            FunctionContext executionContext)
        {
            // Get our client ID for the redirect URL
            var SlackClientId = Settings.GetSetting(Settings.SlackClientId);

            // We generate the destination URL here
            var SlackURL = $"{SignInWithSlackFunction.SlackAuthorize}?scope=chat:write&user_scope=users.profile:write&client_id={SlackClientId}";

            // Create the redirect response (Direct Install URL)
            var response = req.CreateResponse(HttpStatusCode.TemporaryRedirect);
            response.Headers.Add("Location", SlackURL);

            // Deliver the response
            return response;
        }
        
        [Function("SlackCallback")]
        public static HttpResponseData SlackCallback([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req,
            FunctionContext executionContext)
        {
            // We should receive bacK
            // code = Code to turn into tokens
            // state = Empty string
            var queryDictionary = HttpUtility.ParseQueryString(req.Url.Query);

            // Make sure we have a code (return 400 Bad Request otherwise)
            // Normally we would check for they key to exist first, then check for a null/empty string
            // but this function will return null when a key is not found protecting us from an error
            var code = queryDictionary.Get("code")?.Trim();
            if (string.IsNullOrEmpty(code))
                return req.CreateResponse(HttpStatusCode.BadRequest);

            // We have a code, now we have to send this information to Slack so we can get an access token for this user
            
            // Get our client ID for the redirect URL
            var SlackClientId = Settings.GetSetting(Settings.SlackClientId);

            // Get our client secret for the redirect URL
            var SlackClientSecret = Settings.GetSetting(Settings.SlackClientSecret);

            // Generate our form response
            var form = new Dictionary<string, string>();
            form.Add("client_id", SlackClientId);
            form.Add("client_secret", SlackClientSecret);
            form.Add("code", code);

            // Build the request
            var request = new HttpRequestMessage(HttpMethod.Post, SignInWithSlackFunction.SlackAccess)
            {
                // Casted to avoid a warning
                Content = new FormUrlEncodedContent((IEnumerable<KeyValuePair<string?,string?>>) form)
            };

            // Store our response for later
            OAuthV2Authorize? jsonResponse = null;

            using (var client = new HttpClient())
            {
                using (var response = client.Send(request))
                {
                    // Make sure we have a good response
                    if (!response.IsSuccessStatusCode)
                    {
                        return req.CreateResponse(HttpStatusCode.BadRequest);
                    }

                    // Get the contents
                    var contents = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    // This should be valid JSON
                    jsonResponse = JsonSerializer.Deserialize<OAuthV2Authorize>(contents);

                    // If that throws an error we didn't have a valid object response
                    // Check for a null response as well
                    if (jsonResponse == null)
                    {
                        return req.CreateResponse(HttpStatusCode.BadRequest);
                    }
                }
            }

            // Verify we have a good response
            if (jsonResponse == null || !jsonResponse.ok || jsonResponse.authed_user == null || jsonResponse.team == null)
            {
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            // We have been provided a good response
            // Add this user to our table
            TableHandler.InsertOrUpdateUser(jsonResponse);

            // Deliver the response
            // We will deep-link to our app's home
            var destinationUrl = $"slack://app?team={jsonResponse.team.id}&id={jsonResponse.authed_user.id}&tab=home";

            // Create our response structure
            var httpResponse = req.CreateResponse(HttpStatusCode.OK);

            // A simple HTML body to be user friendly... sort of
            httpResponse.WriteString($"<html><head><meta http-equiv='Refresh' content='0; URL={destinationUrl}' /><title>Randomize Avatar</title></head><body><p style='text-align: center;'>You've successfully logged into the Randomize Avatar app. <a href='{destinationUrl}'>Click here</a> to open Slack.</p></body></html>");

            return httpResponse;
        }
    }

    /// <summary>
    /// This is the expected format of a response to the API per https://api.slack.com/methods/oauth.v2.access
    /// </summary>
    public class OAuthV2Authorize
    {
        public bool ok { get; set; }
        public string error { get; set; } = string.Empty;
        public string? app_id { get; set; } = null;
        public OAuthV2AuthorizeAuthedUser? authed_user { get; set; } = null;
        public OAuthV2AuthorizeTeam? team { get; set; } = null;
        public bool is_enterprise_install { get; set; } = false;

        // Bot items
        public string? access_token { get; set; } = null;
        public string? token_type { get; set; } = null;
        public string? bot_user_id { get; set; } = null;
    }

    /// <summary>
    /// This is the expected format of a authed_user to the API per https://api.slack.com/methods/oauth.v2.access
    /// </summary>
    public class OAuthV2AuthorizeAuthedUser
    {
        public string id { get; set; } = string.Empty;
        public string scope { get; set; } = string.Empty;
        public string access_token { get; set; } = string.Empty;
        public string token_type { get; set; } = string.Empty;
    }

    /// <summary>
    /// This is the expected format of a team to the API per https://api.slack.com/methods/oauth.v2.access
    /// </summary>
    public class OAuthV2AuthorizeTeam
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
    }
}
#nullable restore
