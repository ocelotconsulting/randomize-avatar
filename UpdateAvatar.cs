using System;
using System.Linq;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

// MemoryStream
using System.IO;

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
