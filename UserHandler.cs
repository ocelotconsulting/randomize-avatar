using System;
using System.Linq;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

// ETag
using Azure;

// Table access
using Azure.Data.Tables;

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

#nullable enable
namespace OcelotConsulting.Avatars
{
    public static class UserHandler
    {
        public static bool InsertOrUpdateUser(OAuthV2Authorize authedResponse)
        {
            // Make sure we have a good response
            if (authedResponse == null)
                throw new ArgumentNullException(paramName: nameof(authedResponse));

            if (!authedResponse.ok || authedResponse.authed_user == null || authedResponse.team == null
                || string.IsNullOrEmpty(authedResponse.authed_user.id) || string.IsNullOrEmpty(authedResponse.team.id))
                throw new Exception($"The provided authenticated response is not valid.");

            // Get access to our table, if there are failures in creating or checking existence, it will error out
            var tableClient = UserHandler.GetTableClient();
            
            // Create the user object
            // By using upsert below, we don't have to look for existing objects and differentiate between insert/update
            var user = new UserEntity(authedResponse.authed_user.id, authedResponse.team.id)
            {
                valid = true,
                accessToken = authedResponse.authed_user.access_token
            };

            // Update an existing entity if it exists or insert a new one
            tableClient.UpsertEntity<UserEntity>(user);

            return true;
        }

        /// <summary>
        /// Provide a list of users queried from the Table including those in an error state if requested
        /// </summary>
        /// <param name="includeErrors"><c>true</c> to include all users, even those in an error state (Default: <c>false</c>)</param>
        /// <returns><see cref="System.Collections.Generic.List{T}"/> of <see cref="OcelotConsulting.Avatars.UserEntity"/> results</returns>
        public static List<UserEntity> GetUsers(bool includeErrors = false)
        {
            // A basic LINQ expression makes this query very easy
            return UserHandler.GetTableClient()
                .Query<UserEntity>(a => includeErrors || a.valid)
                .ToList();
        }

        private static UserEntity? GetUser(string user_id, string team_id, TableClient? tableClient = null)
        {
            // Check that we have parametesr
            if (string.IsNullOrEmpty(user_id.Trim()))
                throw new ArgumentNullException(paramName: nameof(user_id));
                
            if (string.IsNullOrEmpty(team_id.Trim()))
                throw new ArgumentNullException(paramName: nameof(team_id));

            // Get a table client if we don't have one already
            if (tableClient == null)
                tableClient = GetTableClient();

            // Now try to query for the user
            // By using the strongly-typed CreateQueryFilter, we can use LINQ expressions to make this easier
            return tableClient.Query<UserEntity>(filter: TableClient.CreateQueryFilter<UserEntity>(a => a.team_id == team_id && a.user_id == user_id)).FirstOrDefault();
        }

        /// <summary>
        /// Gets the connection string and returns a <see cref="Azure.Data.Tables.TableServiceClient"/>
        /// </summary>
        /// <returns><see cref="Azure.Data.Tables.TableServiceClient"/></returns>
        private static TableServiceClient GetTableServiceClient()
        {
            // Get our connection string (standard Azure connection string for Functions)
            // Then return the client for that string
            return new TableServiceClient(Settings.GetSetting("AzureWebJobsStorage"));
        }

        /// <summary>
        /// Returns the appropriate <see cref="Azure.Data.Tables.TableClient"/>
        /// </summary>
        /// <returns><see cref="Azure.Data.Tables.TableClient"/></returns>
        private static TableClient GetTableClient()
        {
            // Gets the TableServiceClient and will create the appropriate client
            // information for the individual TableClient
            var tableName = Settings.GetSetting(Settings.UserTableName);

            // We also double check the table exists
            var tableServiceClient = UserHandler.GetTableServiceClient();
            tableServiceClient.CreateTableIfNotExists(tableName);

            return tableServiceClient.GetTableClient(tableName);
        }
    }

    /// <summary>
    /// The User Entity object that defines our User Table
    /// </summary>
    public class UserEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // Actual user properties we care about
        /// <summary>
        /// The RowKey defined by <see cref="OcelotConsulting.Avatars.OAuthV2AuthorizeAuthedUser.id"/>
        /// </summary>
        public string user_id { get => RowKey; set => RowKey = value; }

        /// <summary>
        /// The PartitionKey defined by <see cref="OcelotConsulting.Avatars.OAuthV2AuthorizeTeam.id"/>
        /// </summary>
        public string team_id { get => PartitionKey; set => PartitionKey = value; }

        /// <summary>
        /// The access token we use to operate on this user
        /// </summary>
        public string accessToken { get; set; } = string.Empty;

        /// <summary>
        /// The date and time (UTC) of the last time the avatar was changed
        /// </summary>
        public DateTimeOffset? LastAvatarChange { get; set; } = null;

        /// <summary>
        /// How often we update this user's avatar (Default: 3600)
        /// </summary>
        public int UpdateFrequencySeconds { get; set; } = 3600;

        /// <summary>
        /// We will track if a user has thrown an error or not (so we can skip it later)
        /// </summary>
        public bool valid { get; set; } = true;

        public UserEntity() { }

        /// <summary>
        /// A strongly-typed user object for our User Table
        /// </summary>
        /// <param name="user_id">The <see cref="OcelotConsulting.Avatars.OAuthV2AuthorizeAuthedUser.id"/> of the user</param>
        /// <param name="team_id">The <see cref="OcelotConsulting.Avatars.OAuthV2AuthorizeTeam.id"/> of the user</param>
        public UserEntity(string user_id, string team_id)
        {
            // We partition by team_id (one partition per team)
            PartitionKey = team_id;

            // Then our unique index per partition is our user
            RowKey = user_id;
        }
    }
}
#nullable restore
