using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System;

class WebConfigurator {
    private async Task WriteStringUtf8(Stream stream, string s) {
        var bytes = System.Text.UTF8Encoding.UTF8.GetBytes(s);
        await stream.WriteAsync(bytes, 0, bytes.Length);
    }

    public void ConfigureServices(IServiceCollection services) {
        services.AddDirectoryBrowser();
    }

    public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory) {
        loggerFactory.AddConsole();

        app = app.UseFileServer(new FileServerOptions()
        {
            FileProvider = new PhysicalFileProvider(
                System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "static")),
            RequestPath = new PathString("/static"),
            EnableDirectoryBrowsing = true,
        });

        app.Run(async (context) =>
        {
            var (request, response) = (context.Request, context.Response);
            Console.WriteLine(request.Path);
            if (!request.Path.StartsWithSegments(new PathString("/api"))) {
                response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var body = response.Body;
            response.ContentType = "application/json";
            var pathParts = request.Path.Value.Substring("api/".Length).Split('/').Where(s => !String.IsNullOrWhiteSpace(s));
            var pathRegex = String.Join(".*", pathParts);
            var logger = loggerFactory.CreateLogger(pathRegex);
            var paths = await VsToRoslyn.GetPaths(pathRegex, logger);

            var json = JsonConvert.SerializeObject(paths.ToArray());
            await WriteStringUtf8(body, json);
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
