using System;
using System.Linq;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

// HTTP Client
using System.Net.Http;

// Async work
using System.Threading;
using System.Threading.Tasks;

// Image manipulation
using System.Drawing.Common;

namespace OcelotConsulting.Avatars
{
    public static class UpdateAvatar
    {
        /// <summary>
        /// Our image source which provides back an image/jpeg of a face
        /// </summary>
        public static string ImageSource = "https://thispersondoesnotexist.com/image";

        /// <summary>
        /// The endpoint we are targeting to perform the Slack update (https://api.slack.com/methods/users.setPhoto)
        /// </summary>
        public static string SlackAPI = "https://slack.com/api/users.setPhoto";

        /// <summary>
        /// Minimum X resolution for an image per Slack requirements (512 px)
        /// </summary>
        public static int MinX = 512;
        
        /// <summary>
        /// Minimum Y resolution for an image per Slack requirements (512 px)
        /// </summary>
        public static int MinY = 512;

        /// <summary>
        /// Maximum X resolution for an image per Slack requirements (1024 px)
        /// </summary>
        public static int MaxX = 1024;
        
        /// <summary>
        /// Maximum Y resolution for an image per Slack requirements (1024 px)
        /// </summary>
        public static int MaxY = 1024;

        /// <summary>
        /// The name of the environment variable (app setting) that includes our Slack Token
        /// </summary>
        public static string SlackTokenSettingName = "SlackToken";

        [Function("UpdateAvatar")]
        public static void Run([TimerTrigger("0 0 * * * *")] MyInfo myTimer, FunctionContext context)
        {
            var logger = context.GetLogger("UpdateAvatar");

            // Order:
            // 1. Get our settings to connect to Slack
            // 2. Get the image
            // 3. Update the avatar

            var SlackToken = System.Environment.GetEnvironmentVariable(UpdateAvatar.SlackTokenSettingName, EnvironmentVariableTarget.Process) ?? string.Empty;

            // We have to have some value
            if (string.IsNullOrEmpty(SlackToken))
            {
                throw new ArgumentNullException(SlackTokenSettingName);
            }

            // Verify this is a user token (must start with "xoxp-")
            if (!SlackToken.StartsWith("xoxp-", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new ArgumentException($"Provided token is not a User Token that starts with 'xoxp-'.", paramName: SlackTokenSettingName);
            }

            // Where we will store our image
            byte[] RandomImage = null;

            using (var client = new HttpClient())
            {
                // Start a max timeout of 30 seconds
                var ctx = new CancellationTokenSource(30 * 1000);

                // Perform the request
                RandomImage = client.GetByteArrayAsync(UpdateAvatar.ImageSource, ctx.Token).GetAwaiter().GetResult();

                // Check that we have a result
                if (RandomImage == null || RandomImage.Length == 0)
                {
                    throw new Exception($"Unable to retrieve the random image.");
                }
            }

            // Check our image size
        }
    }

    public class MyInfo
    {
        public MyScheduleStatus ScheduleStatus { get; set; }

        public bool IsPastDue { get; set; }
    }

    public class MyScheduleStatus
    {
        public DateTime Last { get; set; }

        public DateTime Next { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}
