using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Services.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Nop.Web.Infrastructure;

public class ObservabilityStartup : INopStartup
{
    public int Order => 100; // after all core services

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(NopTelemetry.ServiceName))
            .WithTracing(builder =>
            {
                builder
                    .AddSource(NopTelemetry.ServiceName)    // picks up our ActivitySource
                    .AddAspNetCoreInstrumentation()         // auto HTTP spans
                    .AddSource("Npgsql")  //  (postgresql) to capture db spans
                    .AddConsoleExporter()                  
                    .AddOtlpExporter(opt => {
                        opt.Endpoint = new Uri("http://localhost:4317");
                    });
            })

            .WithMetrics(builder =>
            {
                builder
                    .AddMeter(NopTelemetry.ServiceName)     // picks up our Meter
                    .AddAspNetCoreInstrumentation()
                    .AddConsoleExporter()                   
                    .AddPrometheusExporter()
                    .AddOtlpExporter(opt => {
                        opt.Endpoint = new Uri("http://localhost:4317");
                    });
            });
    }

    public void Configure(IApplicationBuilder application)
    {
        application.UseOpenTelemetryPrometheusScrapingEndpoint();
    }
}
