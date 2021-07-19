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

        //[Function("UpdateAvatar")]
        public static void Run([TimerTrigger("0 0 * * * *")] MyInfo myTimer, FunctionContext context)
        {
            // Multi-User Order:
            // 1. Query the table for all users not in an error state
            // 2. Go through each user and check if it is time to update again
            // 3. Perform the updates!

            // Save our trigger time so we do our math off this
            var triggerTime = DateTimeOffset.UtcNow;

            // Get our user list
            var userList = UserHandler.GetUsers();

            // Figure out who we need to update
            // Our padding is +/- 10% of time (in case our timer is triggering early/late)
            var usersToUpdate = new List<UserEntity>();

            foreach(var user in userList)
            {
                // First time we're updating
                if (user.LastAvatarChange == null)
                {
                    usersToUpdate.Add(user);
                }
                else
                {
                    // MATH!
                    var minSeconds = user.UpdateFrequencySeconds * 0.9;
                    var maxSeconds = user.UpdateFrequencySeconds * 1.1;

                    // Get our start and stop times
                    DateTimeOffset rangeStart = user.LastAvatarChange.Value.AddSeconds(minSeconds);
                    DateTimeOffset rangeEnd = user.LastAvatarChange.Value.AddSeconds(maxSeconds);

                    // If this users falls inside our range, we update them
                    if (rangeStart <= triggerTime && triggerTime <= rangeEnd)
                    {
                        usersToUpdate.Add(user);
                    }
                }
            }

            // Now we have a list of users to operate on, let's perform the updates
            foreach (var user in usersToUpdate)
            {
                // We put a try/catch block here because we don't want one user's failure to interfere with someone else
                try
                {
                    var SlackToken = user.accessToken;

                    // Verify this is a user token (must start with "xoxp-")
                    if (!SlackToken.StartsWith("xoxp-", StringComparison.InvariantCultureIgnoreCase))
                    {
                        throw new ArgumentException($"Provided token is not a User Token that starts with 'xoxp-'.", paramName: Settings.SlackToken);
                    }

                    // Get a random image, ensure it fits the format for Slack, and return a byte[] of the PNG contents
                    var PngImage = UpdateAvatar.FormatSlackAvatar(UpdateAvatar.GetRandomImage());

                    // Update the image
                    var jsonResponse = UpdateAvatar.UpdateSlackAvatar(SlackToken, PngImage);

                    if (!jsonResponse.ok)
                        throw new Exception();

                    // We had a good update, need to change their timestamp
                    user.LastAvatarChange = DateTimeOffset.UtcNow;
                } catch {
                    // We will set this user to an error state
                    user.valid = false;
                }

                // Update our user record if possible
                try
                {
                    UserHandler.UpdateUser(user);
                }
                catch { }
            }

            // All done!
        }

        /// <summary>
        /// Gets an image from our source and returns the <see cref="System.Byte[]"/> contents
        /// </summary>
        /// <returns><see cref="System.Byte[]"/> of an image</returns>
        private static byte[] GetRandomImage()
        {
            // The image to return
            byte[] returnImage = null;

            using (var client = new HttpClient())
            {
                // Start a max timeout of 30 seconds
                var ctx = new CancellationTokenSource(30 * 1000);

                // Perform the request
                returnImage = client.GetByteArrayAsync(UpdateAvatar.ImageSource, ctx.Token).GetAwaiter().GetResult();

                // Check that we have a result
                if (returnImage == null || returnImage.Length == 0)
                {
                    throw new Exception($"Unable to retrieve the random image.");
                }
            }

            return returnImage;
        }

        /// <summary>
        /// Performs validation to ensure the image is the appropriate size for Slack avatars per https://api.slack.com/methods/users.setPhoto
        /// </summary>
        /// <param name="inputImage"><see cref="System.Byte[]"/> of an image</param>
        /// <returns><see cref="System.Byte[]"/> of a validated image</returns>
        private static byte[] FormatSlackAvatar(byte[] inputImage)
        {
            // The image to return
            byte[] returnImage = null;

            // Check our image size
            Image image;
            float scale = 1F;

            using (var ms = new MemoryStream(inputImage))
            {
                // Load the image from the memory stream copied from the bytes
                image = Image.FromStream(ms);

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
                    var scaleWidth = (int)(image.Width * scale);
                    var scaleHeight = (int)(image.Height * scale);
                    var scaledBitmap = new Bitmap(scaleWidth, scaleHeight);

                    Graphics graph = Graphics.FromImage(scaledBitmap);
                    graph.InterpolationMode = InterpolationMode.High;
                    graph.CompositingQuality = CompositingQuality.HighQuality;
                    graph.SmoothingMode = SmoothingMode.AntiAlias;
                    graph.FillRectangle(new SolidBrush(Color.Transparent), new RectangleF(0, 0, scaleWidth, scaleHeight));
                    graph.DrawImage(image, new Rectangle(0, 0, scaleWidth, scaleHeight));

                    // Overwrite the image we have in memory
                    image = Image.FromHbitmap(scaledBitmap.GetHbitmap());

                    // Dispose of our resources
                    graph.Dispose();
                    scaledBitmap.Dispose();
                }

                using (var saveStream = new MemoryStream())
                {
                    // Save it to a memory stream
                    // For this, the original stream must still be opened
                    image.Save(saveStream, ImageFormat.Png);

                    // Save the byte array
                    returnImage = saveStream.ToArray();
                }

                // Dispose of our old image
                image.Dispose();
            }

            // Make sure we have a valid image array
            if (returnImage == null || returnImage.Length == 0)
            {
                throw new Exception("Unable to convert the image to a PNG.");
            }

            return returnImage;
        }

        private static UsersSetPhotoResponse UpdateSlackAvatar(string bearerToken, byte[] image)
        {
            // Our JSON object for later
            UsersSetPhotoResponse jsonResponse = null;

            // Attempt to send the request to Slack
            using (var client = new HttpClient())
            {
                // Add our authorization token
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

                // Initialize our multi-part content
                var content = new MultipartFormDataContent();

                // Add the file content
                // Generate a random file name, but ensure we have a name of "image"
                content.Add(new ByteArrayContent(image), "image", $"{Guid.NewGuid()}.png");

                // Form our HTTP request
                // We use this so we can use Send() instead of PostAsync()
                var request = new HttpRequestMessage(HttpMethod.Post, UpdateAvatar.SlackAPI)
                {
                    Content = content
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

            return jsonResponse;
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
