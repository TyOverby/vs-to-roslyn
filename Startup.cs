using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using static Microsoft.AspNetCore.Routing.RoutingHttpContextExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace clean
{
    public class Startup
    {
        private VssCredentials creds = new VssBasicCredential(
            Environment.GetEnvironmentVariable("VSO_USERNAME"),
            Environment.GetEnvironmentVariable("VSO_PERSONAL_ACCESS_TOKEN"));
        private VssConnection connection = null;

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            connection = new VssConnection(new Uri("https://devdiv.visualstudio.com/DefaultCollection"), creds);
            services.AddRouting();
            services.AddDirectoryBrowser();
        }

        private IRouter ApiRoute(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            var builder = new RouteBuilder(app);
            builder.MapGet("api/{builddef}/{component}/{branch}/{build}/{jsonfile}", async context =>
            {
                var routeData = context.GetRouteData();

                var product = routeData.Values["product"] as String;
                var branch = routeData.Values["branch"] as String;
                var build = routeData.Values["build"] as String;
                var buildDefName = routeData.Values["builddef"] as String;
                var component = routeData.Values["component"] as String;
                var jsonFile = routeData.Values["jsonfile"] as String;

                var pathRegex = $"{branch}.*{build}";
                var logger = loggerFactory.CreateLogger($"{buildDefName} {component} {branch} {build}");
                var paths = await VsToRoslyn.GetPathsAsync(connection, branch, build, int.Parse(buildDefName), component, jsonFile, logger);
                var json = JsonConvert.SerializeObject(paths.ToArray());
                await context.Response.WriteAsync(json);
            });

            builder.MapGet("api/listbuilds/{branch}", async context =>
            {
                var routeData = context.GetRouteData();

                var branch = routeData.Values["branch"] as String;
                var logger = loggerFactory.CreateLogger($"{branch}");

                var branches = await VsToRoslyn.GetAllBuildNumbers(connection, branch, logger);
                var json = JsonConvert.SerializeObject(branches);
                await context.Response.WriteAsync(json);
            });

            builder.MapGet("api/allbranches", async context =>
            {
                var logger = loggerFactory.CreateLogger($"allbranches");

                var branches = await VsToRoslyn.GetAllBranches(connection, logger);
                var json = JsonConvert.SerializeObject(branches);
                await context.Response.WriteAsync(json);
            });

            return builder.Build();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }


            app = app.UseFileServer(new FileServerOptions()
            {
                FileProvider = new PhysicalFileProvider(
                    System.IO.Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")),
                RequestPath = new PathString(""),
                EnableDefaultFiles = true,
                EnableDirectoryBrowsing = false,
            });

            app = app.UseRouter(ApiRoute(app, loggerFactory));

            app.Run(context =>
            {
                context.Response.StatusCode = 404;
                return null;
            });
        }
    }
}
