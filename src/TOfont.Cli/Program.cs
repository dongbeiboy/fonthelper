using TOfont.Core.Extraction;
using TOfont.Core.Formatting;
using TOfont.Core.Models;
using TOfont.Core.Parsing;

if (args.Length == 0)
{
    Console.WriteLine("Usage: TOfont.Cli <command> [options]");
    Console.WriteLine("  font   --text <text> --font <family> [--size 16] [--mode row] [--output <file>]");
    Console.WriteLine("  image  --input <path> [--threshold 128] [--output <file>]");
    Console.WriteLine("  batch  --ptl <path> --font <family> [--size 16] [--output <file>]");
    return 0;
}

var command = args[0];
var opts = ParseArgs(args[1..]);

return command switch
{
    "font" => RunFont(opts),
    "image" => RunImage(opts),
    "batch" => RunBatch(opts),
    _ => PrintError($"Unknown command: {command}")
};

static int RunFont(Dictionary<string, string> opts)
{
    var text = GetRequired(opts, "--text");
    var font = GetRequired(opts, "--font");
    var size = GetInt(opts, "--size", 16);
    var mode = GetString(opts, "--mode", "row");
    var output = GetString(opts, "--output", "");

    var extractor = new FontExtractor { FontSize = size };
    var glyphs = extractor.ExtractRange(text, font);
    var fmt = new OutputFormat();
    var formatter = new OutputFormatter(fmt);
    var result = formatter.FormatGlyphs(glyphs);

    if (!string.IsNullOrEmpty(output))
        File.WriteAllText(output, result);
    else
        Console.Write(result);

    return 0;
}

static int RunImage(Dictionary<string, string> opts)
{
    var input = GetRequired(opts, "--input");
    var threshold = GetInt(opts, "--threshold", 128);
    var output = GetString(opts, "--output", "");

    var extractor = new ImageExtractor { Threshold = threshold };
    var matrix = extractor.Extract(input);

    var glyph = new GlyphInfo
    {
        Character = ' ',
        Width = matrix.Width,
        Height = matrix.Height,
        DotData = matrix.Data
    };

    var fmt = new OutputFormat();
    var formatter = new OutputFormatter(fmt);
    var result = formatter.FormatGlyph(glyph, "image");

    if (!string.IsNullOrEmpty(output))
        File.WriteAllText(output, result);
    else
        Console.Write(result);

    return 0;
}

static int RunBatch(Dictionary<string, string> opts)
{
    var ptl = GetRequired(opts, "--ptl");
    var font = GetRequired(opts, "--font");
    var size = GetInt(opts, "--size", 16);
    var output = GetString(opts, "--output", "");

    var reader = new PtlReader();
    var chars = reader.Read(ptl);

    var extractor = new FontExtractor { FontSize = size };
    var allGlyphs = new List<GlyphInfo>();

    foreach (var ch in chars.SelectMany(c => c))
    {
        allGlyphs.Add(extractor.Extract(ch, font));
    }

    var fmt = new OutputFormat();
    var formatter = new OutputFormatter(fmt);
    var result = formatter.FormatGlyphs(allGlyphs);

    if (!string.IsNullOrEmpty(output))
        File.WriteAllText(output, result);
    else
        Console.Write(result);

    return 0;
}

static int PrintError(string msg) { Console.Error.WriteLine(msg); return 1; }

static string GetRequired(Dictionary<string, string> opts, string key)
{
    if (opts.TryGetValue(key, out var val)) return val;
    Console.Error.WriteLine($"Missing required: {key}");
    Environment.Exit(1);
    return "";
}

static string GetString(Dictionary<string, string> opts, string key, string def) =>
    opts.TryGetValue(key, out var val) ? val : def;

static int GetInt(Dictionary<string, string> opts, string key, int def) =>
    opts.TryGetValue(key, out var val) && int.TryParse(val, out var n) ? n : def;

static Dictionary<string, string> ParseArgs(string[] args)
{
    var dict = new Dictionary<string, string>();
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith("--") && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            dict[args[i]] = args[++i];
        else if (args[i].StartsWith("--"))
            dict[args[i]] = "true";
    }
    return dict;
}
