using System.Diagnostics;
using OpenTelemetry;

namespace Nop.Services.Observability;

/// <summary>
/// An OpenTelemetry processor that applies a three-layer selective sanitization
/// policy to every finished Activity (span), ensuring PII is redacted from
/// telemetry while preserving full debugging capability.
///
/// Layer priority (evaluated in order):
///   1. System Passthrough — technical tags are NEVER sanitized.
///   2. PII Blacklist      — tags matching known PII tokens are redacted.
///   3. Complex-Object Whitelist — tags under known entity prefixes are only
///      kept if their suffix is in an explicit allow-list.
/// </summary>
public class PiiSanitizationProcessor : BaseProcessor<Activity>
{
    private const string RedactedValue = "[REDACTED]";

    #region Layer 1 – System / Technical Passthrough

    /// <summary>
    /// Well-known prefixes for system and instrumentation tags.
    /// Any tag starting with one of these is always passed through untouched.
    /// </summary>
    private static readonly string[] SystemPrefixes =
    {
        "http.",
        "db.",
        "net.",
        "otel.",
        "exception.",
        "rpc.",
        "server.",
        "url.",
        "network.",
        "service.",
        "telemetry.",
        "aspnetcore."
    };

    /// <summary>
    /// Exact system tag keys that must never be sanitized.
    /// </summary>
    private static readonly HashSet<string> SystemExactKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "error",
        "status_code",
        "thread.id",
        "thread.name",
        "enduser.id",
        "peer.service",
        "deployment.environment"
    };

    #endregion

    #region Layer 2 – PII Blacklist

    /// <summary>
    /// If a tag key contains any of these tokens (case-insensitive),
    /// it is considered PII and its value is replaced with [REDACTED].
    /// </summary>
    private static readonly string[] PiiTokens =
    {
        "email",
        "phone",
        "password",
        "card",
        "creditcard",
        "ssn",
        "secret",
        "token",
        "firstname",
        "lastname",
        "fullname",
        "dateofbirth",
        "ip_address"
    };

    #endregion

    #region Layer 3 – Complex-Object Whitelist

    /// <summary>
    /// For tags whose keys start with one of these prefixes, only suffixes
    /// present in the corresponding allow-list are kept. All other suffixes
    /// under these prefixes are redacted.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> WhitelistByPrefix =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["customer."] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "id", "role", "customer_role", "language_id", "currency_id"
            },
            ["address."] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "country_id", "state_province_id", "zip_prefix", "city"
            },
            ["billing."] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "country_id", "state_province_id", "zip_prefix"
            },
            ["shipping."] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "country_id", "state_province_id", "zip_prefix", "method"
            }
        };

    /// <summary>
    /// Tag key suffixes that trigger zip-code truncation (first 4 chars + "**").
    /// </summary>
    private static readonly string[] ZipSuffixes = { "zip", "zipcode", "zip_code", "postalcode", "postal_code" };

    #endregion

    /// <inheritdoc />
    public override void OnEnd(Activity activity)
    {
        if (activity is null)
        {
            base.OnEnd(activity!);
            return;
        }

        // We must collect the tags first and then replace, because
        // SetTag while iterating would modify the collection.
        var tagsToUpdate = new List<KeyValuePair<string, string>>();

        foreach (var tag in activity.Tags)
        {
            var key = tag.Key;
            var value = tag.Value;

            if (string.IsNullOrEmpty(key))
                continue;

            // ── Special Exception: db.statement ──────────────────────
            if (string.Equals(key, "db.statement", StringComparison.OrdinalIgnoreCase))
            {
                if (IsValuePiiBlacklisted(value))
                {
                    tagsToUpdate.Add(new KeyValuePair<string, string>(key, RedactedValue));
                }
                continue; // Always skip further processing for db.statement
            }

            // ── Layer 1: System passthrough ──────────────────────────
            if (IsSystemTag(key))
                continue; // never touch system tags

            // ── Layer 2: PII Blacklist ───────────────────────────────
            if (IsPiiBlacklisted(key))
            {
                tagsToUpdate.Add(new KeyValuePair<string, string>(key, RedactedValue));
                continue;
            }

            // ── Layer 3: Complex-object whitelist ────────────────────
            if (TryApplyWhitelist(key, value, out var sanitizedValue))
            {
                if (sanitizedValue != value)
                    tagsToUpdate.Add(new KeyValuePair<string, string>(key, sanitizedValue));
                continue;
            }

            // Tags that don't match any rule are passed through as-is.
        }

        // Apply all collected mutations.
        foreach (var update in tagsToUpdate)
            activity.SetTag(update.Key, update.Value);

        base.OnEnd(activity);
    }

    #region Decision helpers

    /// <summary>
    /// Returns true if the tag key belongs to the system/technical passthrough set.
    /// </summary>
    public static bool IsSystemTag(string key)
    {
        if (SystemExactKeys.Contains(key))
            return true;

        foreach (var prefix in SystemPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the tag key contains any known PII token.
    /// </summary>
    public static bool IsPiiBlacklisted(string key)
    {
        foreach (var token in PiiTokens)
        {
            if (key.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the given value contains any known PII token.
    /// Used specifically to check the contents of db.statement queries.
    /// </summary>
    public static bool IsValuePiiBlacklisted(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        foreach (var token in PiiTokens)
        {
            if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// If the tag key matches a whitelisted entity prefix, checks whether the
    /// suffix is allowed. Returns true if the prefix matched (i.e. the tag is
    /// "claimed" by the whitelist), regardless of whether the value was changed.
    /// </summary>
    public static bool TryApplyWhitelist(string key, string? value, out string sanitizedValue)
    {
        foreach (var (prefix, allowedSuffixes) in WhitelistByPrefix)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var suffix = key[prefix.Length..];

            // Special zip-code truncation
            if (IsZipSuffix(suffix))
            {
                sanitizedValue = TruncateZip(value);
                return true;
            }

            // Allowed suffix → pass through unchanged
            if (allowedSuffixes.Contains(suffix))
            {
                sanitizedValue = value ?? string.Empty;
                return true;
            }

            // Suffix not in allow-list → redact
            sanitizedValue = RedactedValue;
            return true;
        }

        sanitizedValue = value ?? string.Empty;
        return false; // no prefix matched
    }

    private static bool IsZipSuffix(string suffix)
    {
        foreach (var zs in ZipSuffixes)
        {
            if (string.Equals(suffix, zs, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Truncates a zip code to its first 4 characters followed by "**".
    /// </summary>
    public static string TruncateZip(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return RedactedValue;

        return value.Length <= 4
            ? value + "**"
            : value[..4] + "**";
    }

    #endregion
}
