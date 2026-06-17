using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrisKaraokePlayerWpf;

public sealed class KaraokeSong
{
    public string Format { get; set; } = "ORIS_KARAOKE_V1";
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string SourceKfn { get; set; } = "";
    public string Audio { get; set; } = "";
    public string OriginalAudioSource { get; set; } = "";
    public List<KaraokeLine> Lines { get; set; } = new();

    [JsonIgnore]
    public string JsonPath { get; set; } = "";

    [JsonIgnore]
    public string? AudioPath { get; set; }

    public static KaraokeSong Load(string path)
    {
        string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
        var opt = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        KaraokeSong? song = JsonSerializer.Deserialize<KaraokeSong>(json, opt);
        if (song == null) throw new InvalidDataException("JSON nejde načíst jako ORIS karaoke.");

        song.JsonPath = path;
        song.Lines ??= new();

        foreach (var line in song.Lines)
            line.Words ??= new();

        song.RecalculateLineTimes();
        song.ResolveAudioPath();

        return song;
    }

    public void RecalculateLineTimes()
    {
        for (int i = 0; i < Lines.Count; i++)
        {
            var line = Lines[i];
            line.Index = i;
            line.Words ??= new();

            for (int j = 0; j < line.Words.Count; j++)
                line.Words[j].Index = j;

            if (line.Words.Count > 0)
            {
                line.StartMs = line.Words.Min(w => w.TimeMs);
                line.EndMs = line.Words.Max(w => Math.Max(w.EndMs, w.TimeMs));
            }

            if (string.IsNullOrWhiteSpace(line.Text) && line.Words.Count > 0)
                line.Text = string.Join(" ", line.Words.Select(w => w.Text));
        }
    }

    public int FindLineIndexAt(int ms)
    {
        if (Lines.Count == 0) return -1;

        for (int i = 0; i < Lines.Count; i++)
        {
            int start = Lines[i].StartMs;
            int end = i + 1 < Lines.Count
                ? Lines[i + 1].StartMs
                : Math.Max(Lines[i].EndMs, Lines[i].StartMs + 5000);

            if (ms >= start && ms < end) return i;
        }

        return ms < Lines[0].StartMs ? 0 : Lines.Count - 1;
    }

    public void ResolveAudioPath()
    {
        AudioPath = null;
        if (string.IsNullOrWhiteSpace(JsonPath)) return;

        string dir = Path.GetDirectoryName(JsonPath) ?? "";
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(Audio))
        {
            string a = CleanupAudioName(Audio);
            candidates.Add(a);
            candidates.Add(Path.Combine(dir, Path.GetFileName(a)));
        }

        string stem = Path.GetFileNameWithoutExtension(JsonPath)
            .Replace(".karaoke", "", StringComparison.OrdinalIgnoreCase);

        foreach (string ext in new[] { ".mp3", ".wav", ".m4a", ".ogg", ".wma" })
        {
            candidates.Add(Path.Combine(dir, stem + ext));
            candidates.Add(Path.Combine(dir, stem + ext.ToUpperInvariant()));
        }

        foreach (string c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c) && File.Exists(c))
            {
                AudioPath = Path.GetFullPath(c);
                return;
            }
        }

        if (!Directory.Exists(dir)) return;

        string wanted = Path.GetFileName(CleanupAudioName(Audio));
        if (!string.IsNullOrWhiteSpace(wanted))
        {
            string? found = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                .FirstOrDefault(p => Path.GetFileName(p).Equals(wanted, StringComparison.OrdinalIgnoreCase));

            if (found != null)
            {
                AudioPath = Path.GetFullPath(found);
                return;
            }
        }
    }

    public static string CleanupAudioName(string s)
    {
        s = (s ?? "").Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        if (s.Length > 4 && s[1] == ',' && s[3] == ',')
            s = s[4..]; // 1,L,file.mp3

        return s;
    }
}

public sealed class KaraokeLine
{
    public int Index { get; set; }
    public string Text { get; set; } = "";
    public int StartMs { get; set; }
    public int EndMs { get; set; }
    public List<KaraokeWord> Words { get; set; } = new();

    public override string ToString() => Text;
}

public sealed class KaraokeWord
{
    public int Index { get; set; }
    public string Text { get; set; } = "";
    public int TimeMs { get; set; }
    public int EndMs { get; set; }
}
