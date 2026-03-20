using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Nop.Services.Observability;

/// <summary>
/// Central telemetry definitions for nopCommerce.
/// One ActivitySource and one Meter for the whole application.
/// </summary>
public static class NopTelemetry
{
    public const string ServiceName = "nopCommerce";

    // Tracing
    public static readonly ActivitySource ActivitySource = new(ServiceName);

    // Metrics
    public static readonly Meter Meter = new(ServiceName);

    // Custom Metric 1: Checkout Pipeline Duration 
    public static readonly Histogram<double> CheckoutDuration =
        Meter.CreateHistogram<double>(
            name: "nopcommerce.checkout.duration_ms",
            unit: "ms",
            description: "Time taken to execute the full PlaceOrderAsync pipeline");


    // Custom Metric 2: Checkout Attempts (Successes & Failures)
    public static readonly Counter<long> CheckoutCompleted =
        Meter.CreateCounter<long>(
            name: "nopcommerce.checkout.completed",
            unit: "{order}",
            description: "Number of checkout attempts completed, tagged by success or failure");

}





