using System;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

class Program
{
    const string UserName = "PUT YOUR VSO USER NAME HERE";
    const string AccessToken = "PUT YOUR VSO ACCESS TOKEN HERE";

    public static HttpClient JsonVsoClient(String personalAccessToken)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(
                System.Text.ASCIIEncoding.ASCII.GetBytes(
                    String.Format("{0}:{1}", UserName, personalAccessToken))));
        return client;
    }
    public static HttpClient PlainTextVsoClient(String personalAccessToken)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/plain"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(
                System.Text.ASCIIEncoding.ASCII.GetBytes(
                    String.Format("{0}:{1}", UserName, personalAccessToken))));
        return client;
    }

    public static async Task<String> GetString(HttpClient client, String url)
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

    public static async Task<dynamic> GetJson(HttpClient client, String url)
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

    static async Task<ImmutableArray<String>> FindTagByBuildNumber(HttpClient client , String buildNumber) {
        var refs = await GetJson(client, "https://devdiv.visualstudio.com/DefaultCollection/_apis/git/repositories/a290117c-5a8a-40f7-bc2c-f14dbe3acf6d/refs");
        var builder = ImmutableArray.CreateBuilder<String>();
        var regex = new Regex(buildNumber);
        foreach (var gitref in refs.value) {
            string name = gitref.name.ToString();
            if (regex.IsMatch(name)) {
                builder.Add(name);
            }
        }
        return builder.ToImmutableArray();
    }

    static Regex branchBuildRegex = new Regex("roslyn/(.+)/(.+);");
    static async Task<(String branch, String build)> GetRoslynBuildInfo(HttpClient client, String tag) {
        tag = tag.Replace("refs/tags/", "").Replace("refs/heads/", "");
        var componentsJsonResult = await GetString(client,
            "https://devdiv.visualstudio.com/DefaultCollection/_apis/git/repositories/a290117c-5a8a-40f7-bc2c-f14dbe3acf6d/items?api-version=1.0"
                + "&scopePath=%2F.corext%2FConfigs%2Fcomponents.json"
                + "&versionType=Tag"
                + $"&version={System.Net.WebUtility.UrlEncode(tag)}");
        dynamic obj = JObject.Parse(componentsJsonResult);
        string compilersUrl = obj.Components["Microsoft.CodeAnalysis.Compilers"].url;
        var match = branchBuildRegex.Match(compilersUrl);
        return (match.Groups[1].Value, match.Groups[2].Value);
    }

    static async Task<int> GetRoslynBuildDefinition(HttpClient client) {
        var definitions = await GetJson(client,"https://devdiv.visualstudio.com/DefaultCollection/DevDiv/_apis/build/definitions?api-version=2.0");
        foreach (var definition in definitions.value) {
            string name = definition.name.ToString();
            if (name == "Roslyn-Signed") {
                return definition.id;
            }
        }
        return -1;
    }

    static async Task<ImmutableArray<string>> GetMatchingRoslynBuild(HttpClient client, int roslynBuildDef, string branch, string build) {
        var builds = await GetJson(client, $"https://devdiv.visualstudio.com/DefaultCollection/DevDiv/_apis/build/builds?api-version=2.0&definitions={roslynBuildDef}&statusFilter=completed");
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var buildInstance in builds.value) {
            if (buildInstance.sourceBranch.ToString().EndsWith(branch) && buildInstance.buildNumber.ToString() == build)
            {
                builder.Add(buildInstance.sourceVersion.ToString());
            }
        }
        return builder.ToImmutableArray();
    }

    static async Task MainAsync(string[] args)
    {
        var needle = args[0];
        var jsonClient = JsonVsoClient(AccessToken);
        var textClient = PlainTextVsoClient(AccessToken);

        var gitrefs = await FindTagByBuildNumber(jsonClient, needle);
        // "temp" tags are actually temporary, we will likely fail on the lookup later.
        // Filter them out so we don't report these expected errors.
        gitrefs = gitrefs.Where(r => !r.Contains("temp")).ToImmutableArray();

        Console.WriteLine($"found {gitrefs.Length} tags that match the regex \"{needle}\"");
        foreach (var r in gitrefs) {
            Console.WriteLine(r);
        }
        Console.WriteLine();

        int roslynBuildDef = await GetRoslynBuildDefinition(jsonClient);
        foreach (var tag in gitrefs) {
            Console.WriteLine($"VSO-TAG:     {tag}");

            var (branch, build) = await GetRoslynBuildInfo(textClient, tag);
            Console.WriteLine($"ROSLYN-TAG:  {branch}/{build}");

            foreach (var roslynHash in await GetMatchingRoslynBuild(jsonClient, roslynBuildDef, branch, build)) {
                Console.WriteLine($"ROSLYN-HASH: {roslynHash}");
            }
            Console.WriteLine();
        }
    }

    static void Main(string[] args)
    {
        MainAsync(args).GetAwaiter().GetResult();
    }
}
