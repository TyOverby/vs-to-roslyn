using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;

namespace clean
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDirectoryBrowser();
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
                await context.Response.WriteAsync(json);
            });
        }
    }
}
