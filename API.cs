using System;
using System.Linq;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Immutable;
using Newtonsoft.Json.Linq;

public class Path
{
    public string VsoBuildTag { get; }
    public string VsoBuildDate { get; }
    public string RoslynBuildTag { get; }
    public string RoslynSha { get; }
    public string RoslynBuildDate { get; }

    public Path(string vsoBuildTag, string vsoBuildDate, string roslynBuildTag, DateTime? roslynBuildDate, string roslynSha)
    {
        VsoBuildTag = vsoBuildTag;
        VsoBuildDate = vsoBuildDate;
        RoslynBuildTag = roslynBuildTag;
        RoslynSha = roslynSha;
        if (roslynBuildDate != null)
        {
            var date = roslynBuildDate.Value;
            RoslynBuildDate = $"{date.Year}-{date.Month}-{date.Day}";
        }
        else
        {
            RoslynBuildDate = "unknown";
        }
    }
}

public class VsToRoslyn
{
    public readonly static string PROJECT = "DevDiv";
    public readonly static Guid REPO = new Guid("a290117c-5a8a-40f7-bc2c-f14dbe3acf6d");
    private readonly static Regex branchBuildRegex = new Regex(".+/(.+)/(.+);");

    public static async Task<ImmutableArray<Path>> GetPathsAsync(VssConnection connection, string branch, string build, int buildDef, string component, ILogger logger)
    {
        var gitClient = connection.GetClient<GitHttpClient>();
        var buildClient = connection.GetClient<BuildHttpClient>();

        var allRefs = await gitClient.GetTagRefsAsync(REPO);
        var nonTempRefs = allRefs.Where(r => !r.Name.Contains("temp"));
        var correctNamedRefs = nonTempRefs.Where(r => r.Name.Contains($"{branch}/"))
                                          .Where(r => r.Name.EndsWith(build))
                                          .ToArray();

        if (correctNamedRefs.Length == 0)
        {
            logger.LogWarning($"No VS tags found that mateched {branch}/{build}");
        }

        var jsonFilesAtLocation = await Task.WhenAll(correctNamedRefs.Select(async tag =>
        {
            logger.LogError(tag.Url);
            var desc = new GitVersionDescriptor();
            desc.VersionType = GitVersionType.Tag;
            desc.Version = tag.Name.Replace("refs/tags/", "");
            var stream = await gitClient.GetItemContentAsync(PROJECT, REPO, "/.corext/Configs/components.json", versionDescriptor: desc);
            var streamReader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
            return (Tag: tag, JsonSource: await streamReader.ReadToEndAsync());
        }));

        var buildExtracted = jsonFilesAtLocation.Select(a =>
        {
            dynamic obj = JObject.Parse(a.JsonSource);
            string url = obj["Components"][component]["url"];
            var match = branchBuildRegex.Match(url);
            var result = (match.Groups[1].Value, match.Groups[2].Value);
            return (Tag: a.Tag, Branch: match.Groups[1].Value, Build: match.Groups[2].Value);
        });

        var matchingBuildsCollection = await Task.WhenAll(buildExtracted.Select(async a =>
        {
            var buildDefinitions = new int[] { buildDef };
            var builds = await buildClient.GetBuildsAsync(
                PROJECT, buildDefinitions,
                // buildNumber: a.Build,
                deletedFilter: QueryDeletedOption.IncludeDeleted);
            foreach (var b in builds)
            {
                Console.WriteLine(b.BuildNumber);
            }
            return builds
                .Where(componentBuild => componentBuild.BuildNumber == a.Build)
                .Where(componentBuild => componentBuild.SourceBranch.Contains(a.Branch))
                .Select(componentBuild => (
                    Tag: a.Tag,
                    RoslynBuildDate: componentBuild.FinishTime ?? componentBuild.QueueTime ?? componentBuild.StartTime,
                    Branch: a.Branch,
                    Build: a.Build,
                    Sha: componentBuild.SourceVersion));
        }));


        var matchingBuilds = matchingBuildsCollection.SelectMany(a => a).ToArray();

        if (matchingBuilds.Length == 0)
        {
            logger.LogWarning("No Roslyn builds found");
        }

        return matchingBuilds
            .Select(tuple => new Path(
                vsoBuildTag: tuple.Tag.Name,
                vsoBuildDate: "",
                roslynBuildTag: $"{tuple.Branch}/{tuple.Build}",
                roslynBuildDate: tuple.RoslynBuildDate,
                roslynSha: tuple.Sha))
            .ToImmutableArray();
    }
}
