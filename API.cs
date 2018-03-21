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

    public static async Task<string[]> GetAllBuildNumbers(VssConnection connection, string branch, ILogger logger)
    {
        var gitClient = connection.GetClient<GitHttpClient>();

        var allRefs = await gitClient.GetTagRefsAsync(REPO);
        var nonTempRefs = allRefs.Where(r => !r.Name.Contains("temp"));
        var correctNamedRefs = nonTempRefs.Where(r => r.Name.Contains($"{branch}/"))
                                          .Select(gr => gr.Name)
                                          .Select(name => name.Substring($"refs/tags/drop/{branch}/".Length))
                                          .OrderBy(a => a)
                                          .ToArray();
        return correctNamedRefs;
    }

    public static async Task<string[]> GetAllBranches(VssConnection connection, ILogger logger)
    {
        var gitClient = connection.GetClient<GitHttpClient>();

        var allRefs = await gitClient.GetTagRefsAsync(REPO);
        var nonTempRefs = allRefs.Where(r => !r.Name.Contains("temp"));
        var correctNamedRefs = nonTempRefs.Select(gr => gr.Name)
                                          .Where(name => name.StartsWith("refs/tags/drop/"))
                                          .Select(name => name.Substring("refs/tags/drop/".Length))
                                          .Select(name => name.Split("/")[0])
                                          .ToImmutableHashSet()
                                          .OrderBy(a => a)
                                          .ToArray();
        return correctNamedRefs;
    }

    public static async Task<ImmutableArray<Path>> GetPathsAsync(VssConnection connection, string branch, string build, int buildDef, string component, string jsonFile, ILogger logger)
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

             var componentsStream = await gitClient.GetItemContentAsync(PROJECT, REPO, "/.corext/Configs/components.json", versionDescriptor: desc);
             var componentsStreamReader = new System.IO.StreamReader(componentsStream, System.Text.Encoding.UTF8);

             var customStream = await gitClient.GetItemContentAsync(PROJECT, REPO, $"/.corext/Configs/{jsonFile}.json", versionDescriptor: desc);
             var customStreamReader = new System.IO.StreamReader(customStream, System.Text.Encoding.UTF8);

             return new[] {
                (Tag: tag, JsonSource: await componentsStreamReader.ReadToEndAsync()),
                (Tag: tag, JsonSource: await customStreamReader.ReadToEndAsync()),
            };
         }));

        var buildExtracted = jsonFilesAtLocation.SelectMany(a => a).Select(a =>
        {
            string url;
            try
            {
                dynamic obj = JObject.Parse(a.JsonSource);
                url = obj["Components"][component]["url"];
            }
            catch
            {
                return (Tag: null, Branch: null, Build: null);
            }
            var match = branchBuildRegex.Match(url);
            if (!match.Success)
            {
                return (Tag: null, Branch: null, Build: null);
            }
            var (branchExtr, buildExtr) = (match.Groups[1].Value, match.Groups[2].Value);
            return (Tag: a.Tag, Branch: branchExtr, Build: buildExtr);
        })
        .Where(b => b.Tag != null);

        var matchingBuildsCollection = await Task.WhenAll(buildExtracted.Select(async a =>
        {
            var buildDefinitions = new int[] { buildDef };
            var builds = await buildClient.GetBuildsAsync(
                PROJECT, buildDefinitions,
                deletedFilter: QueryDeletedOption.IncludeDeleted);
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
