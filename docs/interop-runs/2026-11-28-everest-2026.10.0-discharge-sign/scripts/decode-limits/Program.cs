using System.Text.Json;
using System.Text.RegularExpressions;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

// Usage: dec <trace.json> [message-substring ...]
// Decodes every frame whose message name contains one of the substrings and prints the
// discharge-related fields of the decoded record (from its ToString), plus the frame hex.
var path    = args[0];
var filters = args.Length > 1 ? args[1..] : new[] { "ChargeParameterDiscovery" };
using var doc = JsonDocument.Parse(File.ReadAllText(path));
var field = new Regex(@"(EV(?:SE)?M(?:ax|in)imum(?:Dis)?[Cc]harge(?:Power|Current)(?:_L[23])?) = (RationalNumberType \{[^}]*\}|null)");
var seen = new HashSet<string>();
foreach (var ex in doc.RootElement.GetProperty("exchanges").EnumerateArray())
{
    foreach (var side in new[] { "request", "response" })
    {
        if (!ex.TryGetProperty(side, out var m)) continue;
        var name = m.GetProperty("message").GetString() ?? "";
        if (!filters.Any(f => name.Contains(f))) continue;
        var hex   = m.GetProperty("frame").GetString()!;
        var frame = Convert.FromHexString(hex);
        if (!V2GTPDispatcher.TryDecode(frame, out var set, out var message, out var error) || message is null)
        { Console.WriteLine($"#{ex.GetProperty("index")} {name}: decode failed: {error}"); continue; }
        var text = message.ToString() ?? "";
        var hits = field.Matches(text).Select(x => $"{x.Groups[1].Value} = {Compact(x.Groups[2].Value)}").ToList();
        var key  = name + string.Join("|", hits);
        if (!seen.Add(key)) continue;   // print each distinct shape once
        Console.WriteLine($"#{ex.GetProperty("index")} {name} ({set})");
        foreach (var h in hits) Console.WriteLine("    " + h);
        if (hits.Count == 0) Console.WriteLine("    (no limit fields) " + text[..Math.Min(text.Length, 400)]);
        Console.WriteLine("    frame " + hex);
    }
}

static string Compact(string r)
{
    var v = Regex.Match(r, @"Value = (-?\d+)"); var e = Regex.Match(r, @"Exponent = (-?\d+)");
    if (!v.Success) return r;
    var val = long.Parse(v.Groups[1].Value) * Math.Pow(10, e.Success ? int.Parse(e.Groups[1].Value) : 0);
    return $"{val} (value {v.Groups[1].Value}, exp {(e.Success ? e.Groups[1].Value : "0")})";
}
