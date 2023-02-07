using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.OpenApi.Models;
using System.Reflection;

namespace MetaboyApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll",
                    builder =>
                    {
                        builder
                        .AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader();
                    });
            });

            builder.Services.AddControllers();
            
            builder.Services.AddApiVersioning(o =>
            {
                o.AssumeDefaultVersionWhenUnspecified = true;
                o.DefaultApiVersion = new Microsoft.AspNetCore.Mvc.ApiVersion(1,0);
                o.ReportApiVersions = true;
                o.ApiVersionReader = ApiVersionReader.Combine(
                    new QueryStringApiVersionReader("api-version"),
                    new HeaderApiVersionReader("X-Version"),
                    new MediaTypeApiVersionReader("ver"));
            });

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1",
                    new OpenApiInfo
                    {
                        Version = "v1",
                        Title = "MetaBoy API",
                        Description = "API for the MetaBoy project",
                        Contact = new OpenApiContact
                        {
                            Name = "MetaBoy NFT Github",
                            Url = new Uri("https://github.com/MetaboyNFT")
                        }
                    });
                c.SwaggerDoc("v2",
                    new OpenApiInfo
                    {
                        Version = "v2",
                        Title = "MetaBoy API",
                        Description = "API for the MetaBoy project",
                        Contact = new OpenApiContact
                        {
                            Name = "MetaBoy NFT Github",
                            Url = new Uri("https://github.com/MetaboyNFT")
                        }
                    });
                c.SwaggerDoc("v3",
                    new OpenApiInfo
                    {
                        Version = "v3",
                        Title = "MetaBoy API",
                        Description = "API for the MetaBoy project",
                        Contact = new OpenApiContact
                        {
                            Name = "MetaBoy NFT Github",
                            Url = new Uri("https://github.com/MetaboyNFT")
                        }
                    });
                

                var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                c.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
                c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
            });
            
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            

            var app = builder.Build();

            app.UseCors("AllowAll");
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "MetaBoy API v1");
                c.SwaggerEndpoint("/swagger/v2/swagger.json", "MetaBoy API v2");
                c.SwaggerEndpoint("/swagger/v3/swagger.json", "MetaBoy API v3");
            });

            // Configure the HTTP request pipeline.

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}