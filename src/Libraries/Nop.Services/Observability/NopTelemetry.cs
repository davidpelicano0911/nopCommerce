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

    // ── Custom Metric 1: Checkout Pipeline Duration ──
    // Histogram that measures the duration of PlaceOrderAsync in milliseconds.
    // Rationale: if the median is rising, the operator knows that the checkout
    // pipeline is degrading BEFORE users see visible errors
    // (30s timeouts). The p95 of this metric is the first warning sign.
    public static readonly Histogram<double> CheckoutDuration =
        Meter.CreateHistogram<double>(
            name: "nopcommerce.checkout.duration_ms",
            unit: "ms",
            description: "Time taken to execute the full PlaceOrderAsync pipeline");

    // ── Custom Metric 2: Inventory Adjustment Failures ──
    // Counter that records how many times AdjustInventory failed during checkout.
    // Rationale: if stock went negative or the adjustment threw an exception,
    // the operator knows there is a risk of overselling. A rising counter
    // indicates a data problem in the catalog that should be investigated
    // before it turns into customer complaints.
    public static readonly Counter<long> InventoryAdjustmentFailures =
        Meter.CreateCounter<long>(
            name: "nopcommerce.inventory.adjustment_failures",
            unit: "{failure}",
            description: "Number of inventory adjustments that failed during order placement");
}





