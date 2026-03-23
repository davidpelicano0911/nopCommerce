using System.Diagnostics;
using NUnit.Framework;
using Nop.Services.Observability;

namespace Nop.Tests.Nop.Services.Tests.Observability;

[TestFixture]
public class PiiSanitizationProcessorTests
{
    private PiiSanitizationProcessor _processor;
    private ActivitySource _testSource;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _testSource = new ActivitySource("Test.PiiSanitization");
        // Ensure activities are created (not sampled-out) during tests.
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => source.Name == _testSource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        });
    }

    [SetUp]
    public void SetUp()
    {
        _processor = new PiiSanitizationProcessor();
    }

    #region Helpers

    /// <summary>
    /// Creates a started Activity, sets the given tags, and runs OnEnd through the processor.
    /// Returns the same Activity so tests can inspect tag values afterwards.
    /// </summary>
    private Activity CreateAndProcess(params (string Key, string Value)[] tags)
    {
        var activity = _testSource.StartActivity("test-span")!;
        foreach (var (key, value) in tags)
            activity.SetTag(key, value);

        _processor.OnEnd(activity);
        return activity;
    }

    private static string? GetTag(Activity activity, string key)
    {
        foreach (var tag in activity.Tags)
        {
            if (tag.Key == key)
                return tag.Value;
        }
        return null;
    }

    #endregion

    // ──────────────────────────────────────────────────────────────────
    // Layer 2 – PII Blacklist
    // ──────────────────────────────────────────────────────────────────

    [TestCase("customer.email", "alice@example.com")]
    [TestCase("user.email", "bob@test.org")]
    [TestCase("billing.phone", "+351912345678")]
    [TestCase("customer.phone", "555-0100")]
    [TestCase("password", "hunter2")]
    [TestCase("password_hash", "sha256:abcdef")]
    [TestCase("card_number", "4111111111111111")]
    [TestCase("card_cvv", "123")]
    [TestCase("creditcard_number", "5500000000000004")]
    [TestCase("customer.ssn", "123-45-6789")]
    [TestCase("api_secret", "sk_live_abc123")]
    [TestCase("auth_token", "eyJhbGciOiJIUzI1NiJ9")]
    [TestCase("customer.firstname", "David")]
    [TestCase("customer.lastname", "Pelicano")]
    [TestCase("customer.dateofbirth", "1990-01-01")]
    public void Blacklist_Redacts_PII_Fields(string key, string value)
    {
        var activity = CreateAndProcess((key, value));
        Assert.That(GetTag(activity, key), Is.EqualTo("[REDACTED]"));
    }

    // ──────────────────────────────────────────────────────────────────
    // Layer 3 – Complex-Object Whitelist
    // ──────────────────────────────────────────────────────────────────

    [TestCase("customer.id", "42")]
    [TestCase("customer.role", "Registered")]
    [TestCase("customer.customer_role", "Admin")]
    [TestCase("customer.language_id", "1")]
    [TestCase("customer.currency_id", "3")]
    [TestCase("address.country_id", "1")]
    [TestCase("address.city", "Porto")]
    [TestCase("shipping.method", "UPS Ground")]
    public void Whitelist_Allows_NonIdentifiable_Attributes(string key, string value)
    {
        var activity = CreateAndProcess((key, value));
        Assert.That(GetTag(activity, key), Is.EqualTo(value));
    }

    [TestCase("customer.name", "David Pelicano")]
    [TestCase("address.street", "Rua das Flores 1")]
    [TestCase("billing.full_name", "David P.")]
    [TestCase("shipping.address_line1", "123 Main St.")]
    public void Whitelist_Redacts_NonAllowed_Suffixes(string key, string value)
    {
        var activity = CreateAndProcess((key, value));
        Assert.That(GetTag(activity, key), Is.EqualTo("[REDACTED]"));
    }

    [TestCase("address.zipcode", "12345", "1234**")]
    [TestCase("address.zip", "4000-123", "4000**")]
    [TestCase("billing.zip_code", "90210", "9021**")]
    [TestCase("shipping.postalcode", "SW1A", "SW1A**")]
    [TestCase("address.postal_code", "123", "123**")]
    public void Whitelist_Truncates_ZipCode_To_4_Digits(string key, string value, string expected)
    {
        var activity = CreateAndProcess((key, value));
        Assert.That(GetTag(activity, key), Is.EqualTo(expected));
    }

    // ──────────────────────────────────────────────────────────────────
    // Layer 1 – System / Technical Passthrough (and db.statement exception)
    // ──────────────────────────────────────────────────────────────────

    [TestCase("SELECT * FROM Customer WHERE Email = @email")]
    [TestCase("UPDATE Users SET Phone = '123'")]
    [TestCase("INSERT INTO Logs (Message, PasswordHash) VALUES ('abc', 'hash')")]
    [TestCase("SELECT card_number FROM Payments")]
    [TestCase("EXEC GetCustomerBySsn @ssn='123'")]
    public void DbStatement_WithPiiToken_IsRedacted(string query)
    {
        var activity = CreateAndProcess(("db.statement", query));
        Assert.That(GetTag(activity, "db.statement"), Is.EqualTo("[REDACTED]"));
    }

    [TestCase("SELECT * FROM Category WHERE Published = 1")]
    [TestCase("UPDATE Product SET StockQuantity = 10")]
    [TestCase("DELETE FROM ShoppingCartItem WHERE Id = 5")]
    public void DbStatement_WithoutPiiToken_IsPreserved(string query)
    {
        var activity = CreateAndProcess(("db.statement", query));
        Assert.That(GetTag(activity, "db.statement"), Is.EqualTo(query));
    }

    [TestCase("http.method", "POST")]
    [TestCase("http.url", "https://store.com/checkout")]
    [TestCase("http.status_code", "200")]
    [TestCase("db.statement", "SELECT * FROM Customer WHERE Id = @p0")]
    [TestCase("db.system", "mssql")]
    [TestCase("db.name", "nopCommerce")]
    [TestCase("net.peer.name", "sql-server-01")]
    [TestCase("net.peer.port", "1433")]
    [TestCase("otel.status_code", "OK")]
    [TestCase("otel.status_description", "Success")]
    [TestCase("exception.type", "System.NullReferenceException")]
    [TestCase("exception.message", "Object reference not set")]
    [TestCase("exception.stacktrace", "at Nop.Services.Orders.OrderProcessingService.PlaceOrderAsync()")]
    [TestCase("rpc.system", "grpc")]
    [TestCase("rpc.method", "GetCustomer")]
    [TestCase("error", "true")]
    [TestCase("status_code", "500")]
    [TestCase("thread.id", "42")]
    [TestCase("thread.name", "worker-3")]
    public void SystemTag_NeverBlocked(string key, string value)
    {
        var activity = CreateAndProcess((key, value));
        Assert.That(GetTag(activity, key), Is.EqualTo(value),
            $"System tag '{key}' should never be sanitized.");
    }

    /// <summary>
    /// Edge case: a system-prefixed tag that accidentally contains a PII token
    /// (e.g. "http.token_endpoint") must still be preserved because Layer 1
    /// (system passthrough) takes priority over Layer 2 (PII blacklist).
    /// </summary>
    [Test]
    public void SystemTag_OverridesBlacklist_WhenKeyContainsPiiToken()
    {
        var activity = CreateAndProcess(("http.token_endpoint", "/connect/token"));
        Assert.That(GetTag(activity, "http.token_endpoint"), Is.EqualTo("/connect/token"),
            "System tags must pass through even if they contain a PII blacklist token.");
    }

    // ──────────────────────────────────────────────────────────────────
    // Uncategorised tags – should pass through unchanged
    // ──────────────────────────────────────────────────────────────────

    [TestCase("order.total", "149.99")]
    [TestCase("order.id", "10045")]
    [TestCase("product.id", "3")]
    [TestCase("basket.quantity", "2")]
    [TestCase("store.id", "1")]
    [TestCase("event.type", "OrderPlacedEvent")]
    [TestCase("payment.success", "true")]
    public void NonPrefixed_NonBlacklist_Tags_Are_Preserved(string key, string value)
    {
        var activity = CreateAndProcess((key, value));
        Assert.That(GetTag(activity, key), Is.EqualTo(value),
            $"Business tag '{key}' should pass through unchanged.");
    }

    // ──────────────────────────────────────────────────────────────────
    // Internal helpers – direct unit tests
    // ──────────────────────────────────────────────────────────────────

    [Test]
    public void TruncateZip_Null_Returns_Redacted()
    {
        Assert.That(PiiSanitizationProcessor.TruncateZip(null), Is.EqualTo("[REDACTED]"));
    }

    [Test]
    public void TruncateZip_Empty_Returns_Redacted()
    {
        Assert.That(PiiSanitizationProcessor.TruncateZip(""), Is.EqualTo("[REDACTED]"));
    }

    [Test]
    public void TruncateZip_Short_Appends_Stars()
    {
        Assert.That(PiiSanitizationProcessor.TruncateZip("12"), Is.EqualTo("12**"));
    }

    [Test]
    public void TruncateZip_ExactlyFour_Appends_Stars()
    {
        Assert.That(PiiSanitizationProcessor.TruncateZip("1234"), Is.EqualTo("1234**"));
    }

    [Test]
    public void TruncateZip_Long_Truncates_And_Appends_Stars()
    {
        Assert.That(PiiSanitizationProcessor.TruncateZip("12345-678"), Is.EqualTo("1234**"));
    }
}
