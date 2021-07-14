using System;
using System.Linq;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

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
    public static class Settings
    {
        /// <summary>
        /// The name of the environment variable (app setting) that includes our Slack Client ID
        /// </summary>
        public static string SlackClientId = "SlackClientId";
        
        /// <summary>
        /// The name of the environment variable (app setting) that includes our Slack Client Secret
        /// </summary>
        public static string SlackClientSecret = "SlackClientSecret";

        /// <summary>
        /// The name of the environment variable (app setting) that includes our Slack Token
        /// </summary>
        public static string SlackToken = "SlackToken";

        /// <summary>
        /// A helper function to get our app settings as specified.
        /// </summary>
        /// <param name="settingName">The environment variable (app setting) name to look for.</param>
        /// <param name="defaultValue">A default value to return if none is specified (Default: <c>null</c>)</param>
        /// <param name="errorOnEmpty">The function will throw an error if the value is an empty string (not null) (Default: <c>true</c>)</param>
        /// <returns>A string value of the setting</returns>
        public static string GetSetting(string settingName, string? defaultValue = null, bool errorOnEmpty = true)
        {
            // Get the setting value (could be blank), will return null if the setting was not found
            string? settingValue = System.Environment.GetEnvironmentVariable(settingName, EnvironmentVariableTarget.Process);

            // Override with our default value if we have one
            if (string.IsNullOrEmpty(settingValue) && !string.IsNullOrEmpty(defaultValue))
                settingValue = defaultValue;

            // We have to have some value
            // Null always throws an error, empty strings throw an error if errorOnEmpty == true
            if (settingValue == null || (string.IsNullOrEmpty(settingValue) && errorOnEmpty))
            {
                throw new ArgumentNullException(settingName);
            }

            return settingValue;
        }
    }
}
#nullable restore
