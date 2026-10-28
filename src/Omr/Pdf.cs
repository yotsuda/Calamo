using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Omr;

/// <summary>
/// A PDF's pages as images, through Windows' own PDF renderer (Windows.Data.Pdf: no library to ship).
/// Each page is drawn whole at <see cref="Dpi"/> on white, a scan's page image resampled with the rest.
/// </summary>
static class Pdf
{
    public const int Dpi = 300;
    const double MinScanDpi = 150, MaxScanDpi = 600;

    // the renderer sizes its output in the display's pixels, not the asked ones (3472 wide for 2480 at
    // 140 %, and the process, not DPI aware, is told 96): measured on the first page, then asked for less
    static double scale = 1;

    /// <summary>The pages of the PDF, each to the file the name function gives for its number (from 1); the count. Throws on a PDF it cannot open.</summary>
    public static int Render(string path, Func<int, string> pageFile)
    {
        var file = StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask().GetAwaiter().GetResult();
        var doc = PdfDocument.LoadFromFileAsync(file).AsTask().GetAwaiter().GetResult();
        // (the size in DIPs, 1/96 inch, as the page shows: its rotation applied)
        var widths = new List<double>();
        for (uint i = 0; i < doc.PageCount; i++) { using var page = doc.GetPage(i); widths.Add(page.Size.Width / 96); }
        double dpi = ScanDpi(path, widths) ?? Dpi;
        if (dpi != Dpi) Console.WriteLine($"pdf: {Path.GetFileName(path)} a scan at {dpi:F0} dpi, drawn so");
        for (uint i = 0; i < doc.PageCount; i++)
        {
            using var page = doc.GetPage(i);
            int w = (int)Math.Round(page.Size.Width * dpi / 96), h = (int)Math.Round(page.Size.Height * dpi / 96);
            string to = pageFile((int)i + 1);
            int got = Draw(page, w, h, to);
            if (Math.Abs(got - w) > 1)
            {
                scale *= (double)got / (int)Math.Round(w / scale);
                Draw(page, w, h, to);
            }
        }
        return (int)doc.PageCount;
    }

    /// <summary>
    /// A scanned PDF's resolution, to draw its pages at (resampled, a scan read worse: Peters' prelude
    /// at 274 dpi drawn at 300, 88.9 -> 85.1 %); null for a PDF not of scans. A scan is an image of
    /// a megapixel or more for every page; its resolution the images' median width over the pages'.
    /// The images are found by their dictionaries, which, a stream's, are never in a compressed object stream.
    /// </summary>
    static double? ScanDpi(string path, List<double> pageInches)
    {
        string pdf = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path));
        var images = new List<int>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(pdf, @"/Subtype\s*/Image\b"))
        {
            // (the dictionary: from its object's start to its stream)
            int from = pdf.LastIndexOf(" obj", m.Index, Math.Min(m.Index + 1, 4096), StringComparison.Ordinal);
            int to = pdf.IndexOf("stream", m.Index, Math.Min(pdf.Length - m.Index, 4096), StringComparison.Ordinal);
            if (from < 0 || to < 0) continue;
            string dict = pdf[from..to];
            if (System.Text.RegularExpressions.Regex.IsMatch(dict, @"/ImageMask\s*true")) continue;
            var w = System.Text.RegularExpressions.Regex.Match(dict, @"/Width\s+(\d+)(?!\s+\d+\s+R)");
            var h = System.Text.RegularExpressions.Regex.Match(dict, @"/Height\s+(\d+)(?!\s+\d+\s+R)");
            if (!w.Success || !h.Success) continue;
            int iw = int.Parse(w.Groups[1].Value), ih = int.Parse(h.Groups[1].Value);
            if ((double)iw * ih >= 1e6) images.Add(iw);
        }
        if (pageInches.Count == 0 || images.Count < pageInches.Count) return null;
        double Median(IEnumerable<double> xs) { var s = xs.Order().ToList(); return s[s.Count / 2]; }
        double dpi = Median(images.Select(i => (double)i)) / Median(pageInches);
        return Math.Clamp(Math.Round(dpi), MinScanDpi, MaxScanDpi);
    }

    /// <summary>The page drawn to the file at about w × h; the width it came out.</summary>
    static int Draw(PdfPage page, int w, int h, string to)
    {
        var opt = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Round(w / scale),
            DestinationHeight = (uint)Math.Round(h / scale),
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
        };
        using (var ms = new InMemoryRandomAccessStream())
        {
            page.RenderToStreamAsync(ms, opt).AsTask().GetAwaiter().GetResult();
            using var fs = File.Create(to);
            ms.AsStreamForRead().CopyTo(fs);
        }
        // (a PNG's width, big-endian at byte 16 of its IHDR)
        using var png = File.OpenRead(to);
        var head = new byte[24];
        png.ReadExactly(head);
        return head[16] << 24 | head[17] << 16 | head[18] << 8 | head[19];
    }
}
