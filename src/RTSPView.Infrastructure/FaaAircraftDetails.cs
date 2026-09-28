using System.Net;
using System.Text.RegularExpressions;

namespace RTSPView.Infrastructure;

// Public registry fallback for US aircraft. Never infer private ownership from a missing airline.
public static class FaaAircraftDetails
{
    public static bool CanLookup(string registration) => Regex.IsMatch(registration, @"^N[1-9][0-9]{0,4}[A-HJ-NP-Z]{0,2}$", RegexOptions.CultureInvariant)
        && registration.Length <= 6;

    public static AircraftDetail Parse(string html, string hex)
    {
        if (html.Length > 2_000_000 || hex.Length != 6 || !hex.All(Uri.IsHexDigit)) return new();
        string Field(string label)
        {
            var match = Regex.Match(html, "<td\\b[^>]*data-label=\"" + Regex.Escape(label) + "\"[^>]*>([^<]*)</td>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            return WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
        }
        // A reassigned registration or an error page must never enrich the wrong aircraft.
        if (!Field("Mode S Code (Base 16 / Hex)").Equals(hex, StringComparison.OrdinalIgnoreCase)
            || !Field("Status").Equals("Valid", StringComparison.OrdinalIgnoreCase)) return new();
        var kit = Field("Kit Model");
        var model = kit.Length > 0 ? kit : Field("Model");
        var manufacturer = kit.Length > 0 ? Field("Kit Manufacturer") : Field("Manufacturer Name");
        // Kit registrations can list the builder as manufacturer. Use the kit maker when provided.
        if (manufacturer.Equals("CUB CRAFTERS INC", StringComparison.OrdinalIgnoreCase)
            && model.Equals("CCK-1865", StringComparison.OrdinalIgnoreCase))
            model = "CubCrafters Carbon Cub (CCK-1865)";
        else if (model.Length > 0 && manufacturer.Length > 0 && !model.Contains(manufacturer, StringComparison.OrdinalIgnoreCase))
            model = manufacturer + " " + model;
        var owner = Field("Type Registration").Equals("Individual", StringComparison.OrdinalIgnoreCase) ? "Private Owner" : "";
        return new(Owner: owner, Model: model, Source: "FAA");
    }
}
