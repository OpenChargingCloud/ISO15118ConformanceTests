using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using cloud.charging.open.protocols.ISO15118.EXI.Dispatch;

// Usage: dec <trace.json>
// Decodes every ChargeParameterDiscovery, PowerDelivery and ChargeLoop frame of a recorded session with
// our own codec and prints the energy-direction fields of each (power, current, voltage, meter
// registers) as value x 10^exponent. Consecutive messages with identical values are folded into one
// line with a count, so a session reads as its distinct operating points; the first frame of every
// distinct line is printed in hex under it, so each value can be checked against the bytes.
var path = args[0];
using var doc = JsonDocument.Parse(File.ReadAllText(path));

var quantity = new Regex(@"\b(EV(?:SE)?(?:Present|Target|Maximum|Minimum)[A-Za-z]*?(?:Power|Current|Voltage)(?:_L[23])?) = RationalNumberType \{ ([^}]*) \}");
var exponent = new Regex(@"Exponent = (-?\d+)");
var value    = new Regex(@"Value = (-?\d+)");
var register = new Regex(@"\b((?:BPT_)?(?:Charged|Discharged)EnergyReadingWh) = (\d+)");
var mode     = new Regex(@"(?:CLReqControlMode|CLResControlMode|CPDReqEnergyTransferMode|CPDResEnergyTransferMode) = ([A-Za-z_]+?)Type");
var code     = new Regex(@"ResponseCode = (\w+)");

string? lastKey = null; string? lastLine = null; int count = 0;
void Flush()
{
    if (lastLine is null) return;
    Console.WriteLine(count > 1 ? $"{lastLine}\n       … x{count} in a row" : lastLine);
    lastLine = null; count = 0;
}

// An exchange is folded as a whole (request and response together), so a run of identical charge
// loops reads as one pair with a count; the frames differ only in their header (session id, timestamp).
foreach (var ex in doc.RootElement.GetProperty("exchanges").EnumerateArray())
{
    var pairKey = new List<string>(); var pairLines = new List<string>();
    foreach (var side in new[] { "request", "response" })
    {
        if (!ex.TryGetProperty(side, out var m)) continue;
        var name = m.GetProperty("message").GetString() ?? "";
        if (!(name.Contains("ChargeParameterDiscovery") || name.Contains("ChargeLoop") || name.Contains("PowerDelivery"))) continue;
        var hex = m.GetProperty("frame").GetString()!;
        if (!V2GTPDispatcher.TryDecode(Convert.FromHexString(hex), out _, out var message, out var error) || message is null)
        { pairKey.Add(name + " decode failed"); pairLines.Add($"#{ex.GetProperty("index"),3} {name}: decode failed: {error}"); continue; }
        var text = message.ToString() ?? "";

        var fields = new List<string>();
        if (code.Match(text) is { Success: true } rc) fields.Add(rc.Groups[1].Value);
        foreach (Match x in mode.Matches(text)) fields.Add(x.Groups[1].Value);
        foreach (Match x in quantity.Matches(text))
        {
            var body = x.Groups[2].Value;
            var v = decimal.Parse(value.Match(body).Groups[1].Value, CultureInfo.InvariantCulture);
            var e = int.Parse(exponent.Match(body).Groups[1].Value, CultureInfo.InvariantCulture);
            var scaled = e >= 0 ? v * (decimal) Math.Pow(10, e) : v / (decimal) Math.Pow(10, -e);
            fields.Add($"{x.Groups[1].Value}={scaled.ToString("0.###", CultureInfo.InvariantCulture)}");
        }
        foreach (Match x in register.Matches(text)) fields.Add($"{x.Groups[1].Value}={x.Groups[2].Value}");

        pairKey.Add(name + "|" + string.Join(" ", fields));
        pairLines.Add($"#{ex.GetProperty("index"),3} {name,-30} {string.Join("  ", fields)}\n       frame {hex}");
    }
    if (pairLines.Count == 0) continue;
    var key = string.Join("||", pairKey);
    if (key == lastKey) { count++; continue; }
    Flush();
    lastKey  = key;
    count    = 1;
    lastLine = string.Join("\n", pairLines);
}
Flush();
