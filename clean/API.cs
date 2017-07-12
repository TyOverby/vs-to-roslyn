using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using static System.Net.WebUtility;

public class Path {
    public string VsoBuildTag { get; }
    public string RoslynBuildTag { get; }
    public string RoslynSha { get; }

    public Path(string vsoBuildTag, string roslynBuildTag, string roslynSha) {
        VsoBuildTag = vsoBuildTag;
        RoslynBuildTag = roslynBuildTag;
        RoslynSha = roslynSha;
    }
}

public class VsToRoslyn
{
    const string UserName = "tyoverby";
    const string AccessToken = "4fteel7hzhmhs3wburakzbpbsgidwzbxoayzejwvxw5npdbd6d2q";

    private static HttpClient JsonVsoClient(String personalAccessToken)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(
                System.Text.ASCIIEncoding.ASCII.GetBytes(
                    String.Format("{0}:{1}", UserName, personalAccessToken))));
        return client;
    }
    private static HttpClient PlainTextVsoClient(String personalAccessToken)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/plain"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(
                System.Text.ASCIIEncoding.ASCII.GetBytes(
                    String.Format("{0}:{1}", UserName, personalAccessToken))));
        return client;
    }

    private static async Task<String> GetString(HttpClient client, String url)
    {
        using (var response = await client.GetAsync(url))
        {
            try
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            } catch (HttpRequestException e) {
                throw new Exception(url, e);
            }
        }
    }

    private static async Task<dynamic> GetJson(HttpClient client, String url)
    {
        using (var response = await client.GetAsync(url))
        {
            response.EnsureSuccessStatusCode();
            var ret = JObject.Parse(await response.Content.ReadAsStringAsync());
            if (response.Headers.TryGetValues("x-ms-continuationtoken", out var values)) {
                var first = values.FirstOrDefault();
                if (!String.IsNullOrEmpty(first)) {
                    ret["continuation"] = first;
                }
            }
            return ret;
        }
    }

    private static Dictionary<String, ImmutableArray<String>> _findTagByBuildNumberCache = new Dictionary<String, ImmutableArray<String>>();
    private static async Task<ImmutableArray<String>> FindTagByBuildNumber(HttpClient client , String buildNumber) {
        lock (_findTagByBuildNumberCache) {
            if (_findTagByBuildNumberCache.TryGetValue(buildNumber, out var r)) {
                return r;
            }
        }

        var refs = await GetJson(client, "https://devdiv.visualstudio.com/DefaultCollection/_apis/git/repositories/a290117c-5a8a-40f7-bc2c-f14dbe3acf6d/refs");
        var builder = ImmutableArray.CreateBuilder<String>();
        var regex = new Regex(buildNumber);
        foreach (var gitref in refs.value) {
            string name = gitref.name.ToString();
            if (regex.IsMatch(name)) {
                builder.Add(name);
            }
        }
        var result = builder.ToImmutableArray();
        lock (_findTagByBuildNumberCache)
        {
            _findTagByBuildNumberCache.Add(buildNumber, result);
        }
        return result;
    }

    private static Regex branchBuildRegex = new Regex("roslyn/(.+)/(.+);");
    private static Dictionary<String, (String, String)> _getRoslynBuildInfoCache = new Dictionary<String, (String, String)>();
    private static async Task<(String branch, String build)> GetRoslynBuildInfo(HttpClient client, String tag) {
        tag = tag.Replace("refs/tags/", "").Replace("refs/heads/", "");
        lock(_getRoslynBuildInfoCache)
        {
            if (_getRoslynBuildInfoCache.TryGetValue(tag, out var value)) {
                return value;
            }
        }

        var componentsJsonResult = await GetString(client,
            "https://devdiv.visualstudio.com/DefaultCollection/_apis/git/repositories/a290117c-5a8a-40f7-bc2c-f14dbe3acf6d/items?api-version=1.0"
                + "&scopePath=%2F.corext%2FConfigs%2Fcomponents.json"
                + "&versionType=Tag"
                + $"&version={UrlEncode(tag)}");
        dynamic obj = JObject.Parse(componentsJsonResult);
        string compilersUrl = obj.Components["Microsoft.CodeAnalysis.Compilers"].url;
        var match = branchBuildRegex.Match(compilersUrl);
        var result = (match.Groups[1].Value, match.Groups[2].Value);

        lock(_getRoslynBuildInfoCache)
        {
            _getRoslynBuildInfoCache.Add(tag, result);
        }
        return result;
    }

    static int _roslynBuildDefinition = -1;
    private static async Task<int> GetRoslynBuildDefinition(HttpClient client) {
        if (_roslynBuildDefinition != -1) {
            return _roslynBuildDefinition;
        }

        var definitions = await GetJson(client,"https://devdiv.visualstudio.com/DefaultCollection/DevDiv/_apis/build/definitions?api-version=2.0");
        foreach (var definition in definitions.value) {
            string name = definition.name.ToString();
            if (name == "Roslyn-Signed") {
                _roslynBuildDefinition = definition.id;
                return definition.id;
            }
        }
        return -1;
    }

    static Dictionary<(int, string, string), ImmutableArray<string>> _matchingRoslynBuild = new Dictionary<(int, string, string), ImmutableArray<string>>();
    private static async Task<ImmutableArray<string>> GetMatchingRoslynBuild(HttpClient client, int roslynBuildDef, string branch, string build) {
        lock(_matchingRoslynBuild) {
            if (_matchingRoslynBuild.TryGetValue((roslynBuildDef, branch, build), out var r)) {
                return r;
            }
        }

        var builds = await GetJson(client, $"https://devdiv.visualstudio.com/DefaultCollection/DevDiv/_apis/build/builds?api-version=2.0&definitions={roslynBuildDef}&statusFilter=completed");
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var buildInstance in builds.value) {
            if (buildInstance.sourceBranch.ToString().EndsWith(branch) && buildInstance.buildNumber.ToString() == build)
            {
                builder.Add(buildInstance.sourceVersion.ToString());
            }
        }
        var result = builder.ToImmutableArray();
        lock(_matchingRoslynBuild) {
            _matchingRoslynBuild.Add((roslynBuildDef, branch, build), result);
        }
        return result;
    }


    public static async Task<ImmutableArray<Path>> GetPaths(string needle, ILogger logger){
        var jsonClient = JsonVsoClient(AccessToken);
        var textClient = PlainTextVsoClient(AccessToken);
        var builder = ImmutableArray.CreateBuilder<Path>();

        var gitrefs = await FindTagByBuildNumber(jsonClient, needle);
        // "temp" tags are actually temporary, we will likely fail on the lookup later.
        // Filter them out so we don't report these expected errors.
        gitrefs = gitrefs.Where(r => !r.Contains("temp")).ToImmutableArray();

        logger.LogInformation($"found {gitrefs.Length} tags that match the regex \"{needle}\"");
        foreach (var r in gitrefs) {
            logger.LogInformation(r);
        }

        int roslynBuildDef = await GetRoslynBuildDefinition(jsonClient);

        if (roslynBuildDef == -1) {
            logger.LogError("Roslyn Build Definition not found!");
        }
        if (gitrefs.IsEmpty) {
            logger.LogWarning("No git refs found!");
        }

        // Limit to 10 results for sanity
        foreach (var tag in gitrefs.Take(10)) {
            try
            {
                logger.LogInformation($"VSO-TAG:     {tag}");

                var (branch, build) = await GetRoslynBuildInfo(textClient, tag);
                logger.LogInformation($"ROSLYN-TAG:  {branch}/{build}");

                foreach (var roslynHash in await GetMatchingRoslynBuild(jsonClient, roslynBuildDef, branch, build))
                {
                    logger.LogInformation($"ROSLYN-HASH: {roslynHash}");
                    builder.Add(new Path(tag, $"{branch}/{build}", roslynHash));
                }
            }
            catch(Exception e) {
                logger.LogError($"failed for {tag}", e);
            }
        }

        return builder.ToImmutableArray();
    }
}
