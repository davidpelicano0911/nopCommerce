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
                    .AddSqlClientInstrumentation(options => 
                    {
                        options.SetDbStatementForText = true; 
                        options.RecordException = true;       
                    })
                    .AddProcessor<PiiSanitizationProcessor>()
                    .AddOtlpExporter(opt => {
                        var endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
                        opt.Endpoint = new Uri(endpoint);
                    });
            })

            .WithMetrics(builder =>
            {
                builder
                    .AddMeter(NopTelemetry.ServiceName)     // picks up our Meter
                    .AddAspNetCoreInstrumentation()
                    .AddConsoleExporter()                   
                    .AddOtlpExporter(opt => {
                        var endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
                        opt.Endpoint = new Uri(endpoint);
                    });
            });
    }

    public void Configure(IApplicationBuilder application)
    {
        // No longer using Prometheus scraping endpoint; metrics are exported via OTLP to the Collector.
    }
}
