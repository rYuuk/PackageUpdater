using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Networking;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

[InitializeOnLoad]
public static class Updater
{
    //    "com.ryuuk.testpackage" : "https://github.com/rYuuk/TestPackage.git#v0.2.0",
    [Serializable]
    public class Release
    {
        public string tag_name;
        public string name;
    }

    private const string SESSION_STARTED_KEY = "SessionStarted";

    static Updater()
    {
        EditorApplication.update += Update;
    }

    private static void Update()
    {
        if (SessionState.GetBool(SESSION_STARTED_KEY, false)) return;
        SessionState.SetBool(SESSION_STARTED_KEY, true);

        if (SessionState.GetBool("inProgress", false))
        {
            return;
        }
        GetCurrentRelease();
    }

    private static void GetCurrentRelease()
    {
        var packages = AssetDatabase.FindAssets("package") // Get all packages files
            .Select(AssetDatabase.GUIDToAssetPath) // Get path
            .Where(x => x.Contains(".json") && x.Contains("com.ryuuk")) // Get package.json and com.ryuuk packages
            .Select(PackageInfo.FindForAssetPath).ToList();

        if (packages.Count == 0)
        {
            Debug.Log("No com.ryuuk packages found.");
            return;
        }

        var package = packages[0];

        // Get url of repository
        var repoUrl = package.packageId.Substring(package.name.Length + 1);
        // Remove .git from the url and /releases
        var pFrom = repoUrl.IndexOf("https://github.com", StringComparison.Ordinal) + "https://github.com".Length;
        var pTo = repoUrl.LastIndexOf(".git", StringComparison.Ordinal);
        var repoName = repoUrl.Substring(pFrom, pTo - pFrom);

        // Create releases url by adding repoName to api.github url
        var releasesUrl = "https://api.github.com/repos" + repoName + "/releases";

        // remove #version from url
        var packageUrl = repoUrl.Substring(0, repoUrl.Length - 7);
        FetchReleases(package.name, packageUrl, releasesUrl, new Version(package.version));
    }

    private static async void FetchReleases(string packageName, string packageUrl, string releasesUrl, Version currentVersion)
    {
        var request = UnityWebRequest.Get(releasesUrl);
        var async = request.SendWebRequest();
        while (!async.isDone)
        {
            await Task.Yield();
        }

        var response = request.downloadHandler.text;

        var resp = JsonConvert.DeserializeObject<Release[]>(response);
        var versions = new Version[resp!.Length];

        for (int i = 0; i < resp.Length; i++)
        {
            versions[i] = new Version(resp[i].tag_name.Substring(1));
        }

        var latestVersion = versions.Max();

        if (latestVersion > currentVersion)
        {
            PromptForUpdate(packageName, currentVersion, latestVersion, packageUrl);
        }
    }

    private static void PromptForUpdate(string packageName, Version currentVersion, Version latestVersion, string packageUrl)
    {
        packageUrl += "#v" + latestVersion;
        var option = EditorUtility.DisplayDialogComplex("Update Packages",
            $"New update available for {packageName}\nCurrent version: {currentVersion}\nLatest version: {latestVersion}",
            "Update",
            "Cancel",
            "Don't update");

        switch (option)
        {
            // Update.
            case 0:
                Update(packageName, packageUrl, currentVersion, latestVersion);
                break;
            // Cancel.
            case 1:
            // Don't Update
            case 2:
                break;
            default:
                Debug.LogError("Unrecognized option.");
                break;
        }
    }

    private static async void Update(string packageName, string packageUrl, Version currentVersion, Version latestVersion)
    {
        SessionState.SetBool("inProgress", true);
        var removeRequest = Client.Remove(packageName);
        while (!removeRequest.IsCompleted)
        {
            await Task.Yield();
        }

        await Task.Yield();

        Debug.Log("[Updater] " + packageUrl);

        var addRequest = Client.Add(packageUrl);
        while (!addRequest.IsCompleted)
        {
            await Task.Yield();
        }

        Debug.Log($"Updated {packageName} from {currentVersion} to {latestVersion}");
        EditorPrefs.SetBool("inProgress", false);
    }
}
