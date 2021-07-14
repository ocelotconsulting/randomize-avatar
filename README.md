# Randomize Slack Avatar

This is a demo Azure Function application which will update a Slack avatar with a random image from [http://thispersondoesnotexist.com/](http://thispersondoesnotexist.com/).

## Overall Requirements

* Slack Workspace and Account
* Azure Subscription
* Sample code from branch `single-token-poc`

## Slack Requirements

Slack has moved away from [legacy API Tokens](https://api.slack.com/legacy/custom-integrations/legacy-tokens) and requires developers to utilize a [Slack App](https://api.slack.com/start/planning) approach. A Slack App is setup in a specific Slack Workspace and can be [shared with other workspaces](https://api.slack.com/start/distributing/public), however for this demonstration we will only be focused on a personal installation in a single directory.

### Required Permissions

Slack works in an OAuth 2.0 setup where permissions are defined as scopes. For this application we need [users.profile:write](https://api.slack.com/scopes/users.profile:write). This permission allows the application to update the user's profile information (i.e. name, email, etc.) as well as update or remove their avatar. We will utilize the [users.setPhoto](https://api.slack.com/methods/users.setPhoto) web request to do our work.

### Install the Slack App

1. Browse to [https://api.slack.com/apps](https://api.slack.com/apps)
2. Click on "Create New App"
3. Choose "From an app manifest" to continue
4. Choose the appropriate workspace for this application
5. Click Next
6. Paste the contents of `slack-app/manifest.yml` into the `YAML` tab
7. Click Next
8. Review the setup information and click Confirm when you're satisfied

You may be prompted to "Install to Workspace". If so, click the button to do so and follow the prompts before continuing.

### Retrieve the Slack User Access Token

After your app is created, it will appear in your apps list at [https://api.slack.com/apps](https://api.slack.com/apps). Open that app and browse to "OAuth & Permissions" in the menu. Copy the `User OAuth Token` presented at the top of the page. It must begin with `xoxp-` to be valid.

## Azure Infrastructure

Our [Azure Function App](https://docs.microsoft.com/en-us/azure/azure-functions/) only needs the function app itself, a storage account, and an app service plan. We will utilize the consumption plan to keep costs [extremely low](https://azure.microsoft.com/en-us/pricing/details/functions/).

### Setup the Azure Infrastructure

You can utilize the portal to configure you're own Azure Function App, however we have provided a template you can use. You will need to change the names of the resources however.

1. Create a Resource Group to use
2. Open the Resource Group
3. Click Create -> Custom Deployment
4. Click "Build your own template in editor"
5. Paste the contents of `azure/functionapp.json` into the text box and click Save
6. Click "Edit parameters"
7. Paste the contents of `azure/functionapp.parameters.json` into the text box and click Save
   * This step can be skipped if you wish to edit the parameters directly in the portal instead
8. Change the values of the Name, Location, Hosting Plan Name, Storage Account Name, or another parameter as required
9. Click "Review + create"
10. Confirm the parameters look correct and review the terms provided
11. Click Create

### Configure the Function App

In order to access the Slack API, we need to provide the `User OAuth Token` gathered earlier. In a normal setup, this would be stored securely in an `Azure Key Vault`, however this is a proof of concept and the app will be deleted soon so we will simply use the `Function App Application Settings` to save it.

1. Open the Function App in the Azure Portal
2. Click Configuration on the left menu
3. On the Application Settings tab, click "New application setting"
4. Name: `SlackToken`
5. Value: `<Token Copied Earlier>`
6. Click OK
7. Click Save
8. Click Continue

## Deploy the Function App

There are many ways to deploy an Azure Function App. We are going to perform a manual deployment in this example.

1. Build and Publish (prepares for publish) the function app locally: `dotnet publish`
2. Browse to `bin/Debug/5.0/publish`
3. Zip the contents of the folder
4. Open the function App in the Azure Portal
5. Click "Advanced Tools" in the menu
6. Click Go to open a new tab
7. Go to Tools -> Zip Push Deploy
8. Drag and drop, or click Upload, the ZIP file you created earlier
9. Confirm that `slack-avatar.dll` and other files are in the top-level folder, if they're not you may need to recreate the ZIP file
10. Go back to the Function App in the Azure Portal
11. Click Functions in the menu
12. Confirm `UpdateAvatar` is now in the list, you may need to wait 1 minute and click Refresh
13. Open `UpdateAvatar`
14. Click "Code + Test" in the menu
15. Click "Test/Run" to trigger a test
16. Verify the avatar has updated in Slack

In testing, the "Test/Run" button was not always accessible. You can utilize the [manual run method](https://docs.microsoft.com/en-us/azure/azure-functions/functions-manually-run-non-http). Here is a sample cURL request:

```powershell
curl --request POST -H 'x-functions-key:[KEY]' -H "Content-Type:application/json" --data "{}" 'http://[FUNCAPP_NAME].azurewebsites.net/admin/functions/UpdateAvatar'
```

## Local Function App with HTTPS

Run as an Administrator with PowerShell:

```powershell
$cert = New-SelfSignedCertificate -Subject localhost -DnsName localhost -FriendlyName "Functions Development" -KeyUsage DigitalSignature -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1")
Export-PfxCertificate -Cert $cert -FilePath certificate.pfx -Password (ConvertTo-SecureString -String 'fakecertificate' -Force -AsPlainText)
```
