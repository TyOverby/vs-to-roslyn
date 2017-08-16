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

public class VsToRoslyn2 {
    public readonly static string PROJECT = "DevDiv";
    public readonly static Guid REPO = new Guid("a290117c-5a8a-40f7-bc2c-f14dbe3acf6d");
    private readonly static Regex branchBuildRegex = new Regex(".+/(.+)/(.+);");

    public static async Task<ImmutableArray<Path>> GetPathsAsync(VssConnection connection, string branch, string build, int buildDef, string component, ILogger logger) {
        var gitClient = connection.GetClient<GitHttpClient>();
        var buildClient = connection.GetClient<BuildHttpClient>();

        Console.WriteLine("looking for refs");

        var allRefs = await gitClient.GetTagRefsAsync(REPO);
        var nonTempRefs = allRefs.Where(r => !r.Name.Contains("temp"));
        var correctNamedRefs = nonTempRefs.Where(r => r.Name.Contains($"{branch}/") && r.Name.EndsWith(build));
        var taggedRefs = await Task.WhenAll(correctNamedRefs.Select(tag => gitClient.GetAnnotatedTagAsync(PROJECT, REPO, tag.ObjectId)));
        Console.WriteLine("got refs");

        var jsonFilesAtLocation = await Task.WhenAll(taggedRefs.Select(async tag =>
        {
            var desc = new GitVersionDescriptor();
            desc.VersionType = GitVersionType.Commit;
            desc.Version = tag.TaggedObject.ObjectId;
            var stream = await gitClient.GetItemContentAsync(PROJECT, REPO, "/.corext/Configs/components.json", versionDescriptor: desc);
            var streamReader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
            return (Tag: tag, JsonSource: await streamReader.ReadToEndAsync());
        }));

        var buildExtracted = jsonFilesAtLocation.Select(a=> {
            dynamic obj = JObject.Parse(a.JsonSource);
            string url = obj["Components"][component]["url"];
            var match = branchBuildRegex.Match(url);
            var result = (match.Groups[1].Value, match.Groups[2].Value);
            return (Tag: a.Tag, Branch: match.Groups[1].Value, Build: match.Groups[2].Value);
        });
        Console.WriteLine("got json files");

        var matchingBuildsCollection = await Task.WhenAll(buildExtracted.Select(async a => {
            var buildDefinitions = new int[] { buildDef };
            var builds = await buildClient.GetBuildsAsync(
                PROJECT, buildDefinitions,
                //buildNumber: a.Build, branchName: a.Branch,
                deletedFilter: QueryDeletedOption.IncludeDeleted);
            return builds
                .Where(componentBuild => componentBuild.BuildNumber == a.Build)
                .Where(componentBuild => componentBuild.SourceBranch.Contains(a.Branch))
                .Select(componentBuild => (Tag: a.Tag, Branch: a.Branch, Build: a.Build, Sha: componentBuild.SourceVersion));
        }));
        var matchingBuilds = matchingBuildsCollection.SelectMany(a => a);

        return matchingBuilds
            .Select(tuple => new Path(tuple.Tag.Name, $"{tuple.Branch}/{tuple.Build}", tuple.Sha))
            .ToImmutableArray();
    }

    /*
    public static async Task Main() {
        Console.WriteLine("start");
        VssCredentials creds = new VssBasicCredential(
            Environment.GetEnvironmentVariable("VSO_USERNAME"),
            Environment.GetEnvironmentVariable("VSO_PERSONAL_ACCESS_TOKEN"));

            VssConnection connection = new VssConnection(new Uri("https://devdiv.visualstudio.com/DefaultCollection"), creds);

        await GetPathsAsync(connection, "d15rel", "26612.00", 1449, "Microsoft.CodeAnalysis.Compilers", null);
    }*/
}
