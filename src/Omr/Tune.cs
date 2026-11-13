using System.Globalization;
using System.Reflection;

namespace Omr;

/// <summary>
/// A development knob (S60): OMR_TUNE="Detector.WeakDot=0.3,Symbols.TrustScore=0.6" sets those static
/// double fields before the reading, to measure what one threshold does without a build. A name without
/// its class is taken when exactly one class has such a field. An environment variable rather than an
/// argument, so that every reader a batch script starts (check.ps1, run-audiveris.ps1) gets it.
/// </summary>
static class Tune
{
    public static void Apply(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return;
        var types = typeof(Tune).Assembly.GetTypes().Where(t => t.Namespace == "Omr").ToArray();
        foreach (var item in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = item.IndexOf('=');
            if (eq < 0) throw new ArgumentException($"OMR_TUNE: '{item}' is not Name=value");
            string name = item[..eq].Trim(), cls = null, field = name;
            int dot = name.LastIndexOf('.');
            if (dot >= 0) { cls = name[..dot]; field = name[(dot + 1)..]; }
            var hits = types.Where(t => cls == null || t.Name == cls)
                            .Select(t => t.GetField(field, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                            .Where(f => f != null && f.FieldType == typeof(double) && !f.IsLiteral && !f.IsInitOnly)
                            .ToList();
            if (hits.Count != 1) throw new ArgumentException($"OMR_TUNE: '{name}' names {hits.Count} settable double fields");
            hits[0].SetValue(null, double.Parse(item[(eq + 1)..], CultureInfo.InvariantCulture));
        }
    }
}
