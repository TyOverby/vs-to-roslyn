using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Diagnostics;

class WebConfigurator {
    private async Task WriteStringUtf8(Stream stream, string s) {
        var bytes = System.Text.UTF8Encoding.UTF8.GetBytes(s);
        await stream.WriteAsync(bytes, 0, bytes.Length);
    }
    public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory) {
        loggerFactory.AddConsole();
        app.Run(async (context) =>
        {
            var (request, response) = (context.Request, context.Response);
            var body = response.Body;
            response.ContentType = "text/html";
            var pathParts = request.Path.Value.Split('/').Where(s => !String.IsNullOrWhiteSpace(s));
            var pathRegex = String.Join(".*", pathParts);
            var logger = loggerFactory.CreateLogger(pathRegex);
            var paths = await VsToRoslyn.GetPaths(pathRegex, logger);

            await WriteStringUtf8(body, $"<head><title>{String.Join(" ", pathParts)}</title></title>");
            await WriteStringUtf8(body, @"
            <style>
                table, tr, td {
                    border: 1px solid black;
                }
            </style>
            ");


            await WriteStringUtf8(body, "<table style=\"border:1px solid black;\">");
            await WriteStringUtf8(body, "<tr>");
                await WriteStringUtf8(body, "<td><b>VSO Build Tag</b></td>");
                await WriteStringUtf8(body, "<td><b>Roslyn Signed Build Tag</b></td>");
                await WriteStringUtf8(body, "<td><b>Roslyn SHA</b></td>");
            await WriteStringUtf8(body, "</tr>");
            foreach (var path in paths) {
                await WriteStringUtf8(body, "<tr>");
                    await WriteStringUtf8(body, $"<td>{path.VsoBuildTag}</td>");
                    await WriteStringUtf8(body, $"<td>{path.RoslynBuildTag}</td>");
                    await WriteStringUtf8(body, $"<td>{path.RoslynSha}</td>");
                await WriteStringUtf8(body, "</tr>");
            }
            await WriteStringUtf8(body, "</table>");
        });
    }
}

class WebFrontend {
    static void Main(string[] args)
    {
        // MainAsync(args).GetAwaiter().GetResult();
        var host =
            new WebHostBuilder()
            .UseStartup<WebConfigurator>()
            .UseKestrel()
            .UseUrls("http://localhost:8080")
            .UseContentRoot(Directory.GetCurrentDirectory())
            .Build();
        host.Run();
    }
}
