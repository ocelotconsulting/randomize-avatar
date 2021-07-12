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
            Image image;
            float scale = 1F;
            

            using (var ms = new MemoryStream(RandomImage))
            {
                // Load the image from the memory stream copied from the bytes
                image = Image.FromStream(ms);
            }

            // If we didn't have a valid image, that would throw an error
            if (image.Width < UpdateAvatar.MinX || image.Height < UpdateAvatar.MinY)
            {
                // Need to scale up!
                // Shamelessly stolen from: https://stackoverflow.com/a/10445101
                scale = Math.Min(UpdateAvatar.MinX / image.Width, UpdateAvatar.MinY / image.Height);
            }
            else if (image.Width > UpdateAvatar.MaxX || image.Height > UpdateAvatar.MaxY)
            {
                // Need to scale down!
                // Shamelessly stolen from: https://stackoverflow.com/a/10445101
                scale = Math.Max(UpdateAvatar.MaxX / image.Width, UpdateAvatar.MaxY / image.Height);
            }

            // Do we need to scale?
            if (scale != 1F)
            {
                // Shameless stolen from: https://stackoverflow.com/a/49395806
                var scaleWidth  = (int)(image.Width  * scale);
                var scaleHeight = (int)(image.Height * scale);
                var scaledBitmap = new Bitmap(scaleWidth, scaleHeight);

                Graphics graph = Graphics.FromImage(scaledBitmap);
                graph.InterpolationMode = InterpolationMode.High;
                graph.CompositingQuality = CompositingQuality.HighQuality;
                graph.SmoothingMode = SmoothingMode.AntiAlias;
                graph.FillRectangle(new SolidBrush(Color.Transparent), new RectangleF(0, 0, scaleWidth, scaleHeight));
                graph.DrawImage(image, new Rectangle(0, 0 , scaleWidth, scaleHeight));

                // Overwrite the image we have in memory
                image = Image.FromHbitmap(scaledBitmap.GetHbitmap());

                // Dispose of our resources
                graph.Dispose();
                scaledBitmap.Dispose();
            }

            // Slack wants a square photo, so we will square it up from the top-left corner
            int crop_w = Math.Min(image.Width, image.Height);
            int crop_x = 0;
            int crop_y = 0;

            // Now we have an image, let's copy it to a byte array of type PNG
            byte[] PngImage = null;

            using (var ms = new MemoryStream())
            {
                // Save it to a memory stream
                image.Save(ms, ImageFormat.Png);

                // Save the byte array
                PngImage = ms.ToArray();
            }

            // Dispose of our old image
            image.Dispose();

            // Our JSON object for later
            UsersSetPhotoResponse jsonResponse = null;

            // Attempt to send the request to Slack
            using (var client = new HttpClient())
            {
                // Add our authorization token
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SlackToken);

                // Form data
                // We have to send it as x-www-form-urlencoded, so we use a dictionary to build that
                var form = new Dictionary<string, string>();

                // Send numbers in a generic (no commas, periods, etc.) format
                form.Add("crop_w", crop_w.ToString("D"));
                form.Add("crop_x", crop_x.ToString("D"));
                form.Add("crop_y", crop_y.ToString("D"));

                // Form our HTTP request
                // We use this so we can use Send() instead of PostAsync()
                var request = new HttpRequestMessage(HttpMethod.Post, UpdateAvatar.SlackAPI)
                {
                    Content = new FormUrlEncodedContent(form)
                };

                // Perform the request
                using (var response = client.Send(request))
                {
                    // Make sure we have a good response
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"Unsuccessful response from Slack API: {response.StatusCode}");
                    }

                    // Get the contents
                    var contents = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    // This should be valid JSON
                    jsonResponse = JsonSerializer.Deserialize<UsersSetPhotoResponse>(contents);

                    // If that throws an error we didn't have a valid object response
                    // Check for a null response as well
                    if (jsonResponse == null)
                    {
                        throw new HttpRequestException($"Invalid response format from Slack API: {contents}");
                    }
                }
            }

            // Now we can check for our error messages
            if (!jsonResponse.ok)
            {
                // Give the specific error if possible
                if (!string.IsNullOrEmpty(jsonResponse.error))
                    throw new HttpRequestException($"Unsuccessful request with Slack API, see error message: {jsonResponse.error}");
                    
                // Otherwise, generic error
                throw new HttpRequestException($"Unsuccessful request with Slack API and no error message returned.");
            }

            // All done!
        }
    }

    /// <summary>
    /// This is the expected format of a response to the API per https://api.slack.com/methods/users.setPhoto
    /// </summary>
    public class UsersSetPhotoResponse
    {
        /// <summary>
        /// <c>true</c> if the request was successful; otherwise, <c>false</c>
        /// </summary>
        public bool ok { get; set; }

        /// <summary>
        /// Optional string. Could be empty.
        /// </summary>
        public string error { get; set; } = string.Empty;
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
