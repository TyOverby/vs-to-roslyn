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
    static string UserName = System.Environment.GetEnvironmentVariable("VSO_USERNAME");
    static string AccessToken = System.Environment.GetEnvironmentVariable("VSO_PERSONAL_ACCESS_TOKEN");

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

    private static async Task<dynamic> GetJson(HttpClient client, String url, ILogger logger)
    {
        using (var response = await client.GetAsync(url))
        {
            response.EnsureSuccessStatusCode();
            var stringValue = await response.Content.ReadAsStringAsync();
            dynamic ret;
            try
            {
                ret = JObject.Parse(stringValue);
            }
            catch (Exception)
            {
                logger.LogError(stringValue);
                throw;
            }

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
    private static async Task<ImmutableArray<String>> FindTagByBuildNumber(HttpClient client , String buildNumber, ILogger logger) {
        lock (_findTagByBuildNumberCache) {
            if (_findTagByBuildNumberCache.TryGetValue(buildNumber, out var r)) {
                return r;
            }
        }

        var refs = await GetJson(client, "https://devdiv.visualstudio.com/DefaultCollection/_apis/git/repositories/a290117c-5a8a-40f7-bc2c-f14dbe3acf6d/refs", logger);
        var builder = ImmutableArray.CreateBuilder<String>();
        var regex = new Regex(buildNumber, RegexOptions.IgnoreCase);
        foreach (var gitref in refs.value) {
            string name = gitref.name.ToString();
            if (regex.IsMatch(name)) {
                builder.Add(name);
            }
        }
        var result = builder.ToImmutableArray();
        lock (_findTagByBuildNumberCache)
        {
            if (!_findTagByBuildNumberCache.ContainsKey(buildNumber))
            {
                _findTagByBuildNumberCache.Add(buildNumber, result);
            }
        }
        return result;
    }

    private static Regex branchBuildRegex = new Regex(".+/(.+)/(.+);");
    private static Dictionary<(String, String), (String, String)> _getRoslynBuildInfoCache = new Dictionary<(String, String), (String, String)>();
    private static async Task<(String branch, String build)> GetBuildInfo(HttpClient client, String tag, string component) {
        tag = tag.Replace("refs/tags/", "").Replace("refs/heads/", "");
        var key = (tag, component);
        lock(_getRoslynBuildInfoCache)
        {
            if (_getRoslynBuildInfoCache.TryGetValue(key, out var value)) {
                return value;
            }
        }

        var componentsJsonResult = await GetString(client,
            "https://devdiv.visualstudio.com/DefaultCollection/_apis/git/repositories/a290117c-5a8a-40f7-bc2c-f14dbe3acf6d/items?api-version=1.0"
                + "&scopePath=%2F.corext%2FConfigs%2Fcomponents.json"
                + "&versionType=Tag"
                + $"&version={UrlEncode(tag)}");
        dynamic obj = JObject.Parse(componentsJsonResult);
        string compilersUrl = obj.Components[component].url;
        var match = branchBuildRegex.Match(compilersUrl);
        var result = (match.Groups[1].Value, match.Groups[2].Value);

        lock(_getRoslynBuildInfoCache)
        {
            if (!_getRoslynBuildInfoCache.ContainsKey(key))
            {
                _getRoslynBuildInfoCache.Add(key, result);
            }
        }
        return result;
    }

    static Dictionary<string, int> _buildDefinitions = new Dictionary<string, int>();
    private static async Task<int> GetBuildDefinition(HttpClient client, String buildDefName, ILogger logger) {
        lock(_buildDefinitions) {
            if (_buildDefinitions.TryGetValue(buildDefName, out var r)) {
                return r;
            }
        }


        var definitions = await GetJson(client,"https://devdiv.visualstudio.com/DefaultCollection/DevDiv/_apis/build/definitions?api-version=2.0", logger);
        var result = -1;
        foreach (var definition in definitions.value) {
            string name = definition.name.ToString();
            if (name == buildDefName) {
                result = definition.id;
            }
        }

        if (result != -1) {
            lock (_buildDefinitions) {
                if (!_buildDefinitions.ContainsKey(buildDefName))
                {
                    _buildDefinitions.Add(buildDefName, result);
                }
            }
        }
        return result;
    }

    static Dictionary<(int, string, string), ImmutableArray<string>> _matchingRoslynBuild = new Dictionary<(int, string, string), ImmutableArray<string>>();
    private static async Task<ImmutableArray<string>> GetMatchingRoslynBuild(HttpClient client, int roslynBuildDef, string branch, string build, ILogger logger) {
        var key = (roslynBuildDef, branch, build);
        lock(_matchingRoslynBuild) {
            if (_matchingRoslynBuild.TryGetValue(key, out var r)) {
                return r;
            }
        }

        var queryParams = $"definitions={roslynBuildDef}&buildNumber={build}&statusFilter=all&resultFilter=all&reasonFilter=all&deletedFilter=1";
        var requestUrl = $"https://devdiv.visualstudio.com/DefaultCollection/DevDiv/_apis/build/builds?api-version=2.0&{queryParams}";
        logger.LogTrace(requestUrl);
        var builds = await GetJson(client, requestUrl,  logger);
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var buildInstance in builds.value) {
            if (buildInstance.sourceBranch.ToString().EndsWith(branch) && buildInstance.buildNumber.ToString() == build)
            {
                builder.Add(buildInstance.sourceVersion.ToString());
            }
        }
        var result = builder.ToImmutableArray();
        lock(_matchingRoslynBuild) {
            if (!_matchingRoslynBuild.ContainsKey(key))
            {
                _matchingRoslynBuild.Add(key, result);
            }
        }
        return result;
    }


    public static async Task<ImmutableArray<Path>> GetPaths(string needle, string buildDef, string componentName, ILogger logger){
        var jsonClient = JsonVsoClient(AccessToken);
        var textClient = PlainTextVsoClient(AccessToken);
        var builder = ImmutableArray.CreateBuilder<Path>();

        var gitrefs = await FindTagByBuildNumber(jsonClient, needle, logger);
        // "temp" tags are actually temporary, we will likely fail on the lookup later.
        // Filter them out so we don't report these expected errors.
        gitrefs = gitrefs.Where(r => !r.Contains("temp")).ToImmutableArray();
        // If any of the refs are "official", limit our search to just those.
        // Otherwise, leave every ref for people to search through manually.
        if (gitrefs.Any(gr => gr.Contains("official"))) {
            gitrefs = gitrefs.Where(r => r.Contains("official")).ToImmutableArray();
        }

        logger.LogInformation($"found {gitrefs.Length} tags that match the regex \"{needle}\"");
        foreach (var r in gitrefs) {
            logger.LogInformation(r);
        }

        // If the buildDef string is actually the build def number, use that,
        // otherwise perform a lookup.
        int roslynBuildDef;
        if (!Int32.TryParse(buildDef, out roslynBuildDef)) {
            roslynBuildDef = await GetBuildDefinition(jsonClient, buildDef, logger);
        }
        logger.LogInformation($"roslynBuildDef: {roslynBuildDef}");

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

                var (branch, build) = await GetBuildInfo(textClient, tag, componentName);
                logger.LogInformation($"ROSLYN-TAG:  {branch}/{build}");

                foreach (var roslynHash in await GetMatchingRoslynBuild(jsonClient, roslynBuildDef, branch, build, logger))
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
