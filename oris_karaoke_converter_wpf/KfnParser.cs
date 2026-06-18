using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace OrisKaraokeConverter;

public sealed class KfnArchiveEntry
{
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public int Size { get; set; }
    public int Offset { get; set; }
    public int UnpackedSize { get; set; }
    public int Flags { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

public sealed class KfnArchive
{
    public List<KfnArchiveEntry> Entries { get; set; } = new();
}

public static class KfnParser
{
    private static readonly Regex SectionRegex = new(@"^\s*\[(.+?)\]\s*$", RegexOptions.Compiled);
    private static readonly Regex TextKeyRegex = new(@"^Text(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SyncKeyRegex = new(@"^Sync(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WordRegex = new(@"\S+", RegexOptions.Compiled);

    public static KfnSong ParseFile(string kfnPath)
    {
        byte[] bytes = File.ReadAllBytes(kfnPath);
        string ini = ExtractSongIni(bytes);
        return ParseSongIni(ini);
    }

    public static KfnArchive ReadArchive(string kfnPath) => ParseArchive(File.ReadAllBytes(kfnPath));

    public static KfnArchive ParseArchive(byte[] bytes)
    {
        int endh = IndexOf(bytes, Encoding.ASCII.GetBytes("ENDH"), 0);
        if (endh < 0)
            throw new InvalidDataException("KFN neobsahuje značku ENDH.");

        byte[]? flidKey = ExtractFlidKey(bytes, endh);

        int pos = endh + 4;
        if (pos + 9 > bytes.Length)
            throw new InvalidDataException("KFN má neúplnou tabulku souborů.");

        // Za ENDH bývá: 01 FF FF FF FF [počet souborů LE32]
        pos += 1; // marker, obvykle 0x01
        pos += 4; // unknown, obvykle -1
        uint count = ReadUInt32(bytes, ref pos);
        if (count == 0 || count > 256)
            throw new InvalidDataException("KFN má podezřelý počet interních souborů.");

        var meta = new List<KfnArchiveEntry>();
        for (int i = 0; i < count; i++)
        {
            uint nameLen = ReadUInt32(bytes, ref pos);
            if (nameLen == 0 || nameLen > 4096 || pos + nameLen > bytes.Length)
                throw new InvalidDataException("KFN má poškozený název interního souboru.");

            string name = DecodeAnsi(bytes.AsSpan(pos, (int)nameLen).ToArray()).TrimEnd('\0');
            pos += (int)nameLen;

            if (pos + 20 > bytes.Length)
                throw new InvalidDataException("KFN má neúplný záznam interního souboru.");

            var entry = new KfnArchiveEntry
            {
                Name = name,
                Type = unchecked((int)ReadUInt32(bytes, ref pos)),
                Size = unchecked((int)ReadUInt32(bytes, ref pos)),
                Offset = unchecked((int)ReadUInt32(bytes, ref pos)),
                UnpackedSize = unchecked((int)ReadUInt32(bytes, ref pos)),
                Flags = unchecked((int)ReadUInt32(bytes, ref pos))
            };
            meta.Add(entry);
        }

        int dataStart = pos;
        foreach (var entry in meta)
        {
            int storedLength = entry.Flags != 0 && entry.UnpackedSize > 0 ? entry.UnpackedSize : entry.Size;

            long start = (long)dataStart + entry.Offset;
            long end = start + storedLength;
            if (entry.Size < 0 || entry.Offset < 0 || storedLength < 0 || start < 0 || end > bytes.Length)
                throw new InvalidDataException($"Interní soubor {entry.Name} ukazuje mimo KFN.");

            byte[] raw = bytes.AsSpan((int)start, storedLength).ToArray();

            if (entry.Flags != 0)
                entry.Data = DecryptKfnEntry(raw, entry.Size, flidKey, entry.Name);
            else
                entry.Data = raw;
        }

        return new KfnArchive { Entries = meta };
    }

    private static byte[]? ExtractFlidKey(byte[] bytes, int endhOffset)
    {
        int pos = 4; // po KFNB
        while (pos + 9 <= endhOffset)
        {
            string tag = Encoding.ASCII.GetString(bytes, pos, 4);
            pos += 4;

            byte type = bytes[pos++];
            uint lenOrValue = ReadUInt32(bytes, ref pos);

            if (tag == "ENDH")
                break;

            if (type == 2)
            {
                if (lenOrValue > 16 * 1024 || pos + lenOrValue > bytes.Length) return null;

                if (tag == "FLID" && lenOrValue == 16)
                    return bytes.AsSpan(pos, 16).ToArray();

                pos += (int)lenOrValue;
            }
            // type 1 má hodnotu už načtenou v lenOrValue
        }

        return null;
    }

    private static byte[] DecryptKfnEntry(byte[] encrypted, int clearLength, byte[]? flidKey, string entryName)
    {
        if (flidKey == null || flidKey.Length != 16)
            throw new InvalidDataException($"Interní soubor {entryName} je šifrovaný, ale KFN nemá použitelný FLID klíč.");

        if (encrypted.Length % 16 != 0)
            throw new InvalidDataException($"Interní soubor {entryName} má šifrovanou délku, která není násobkem 16 bajtů.");

        using Aes aes = Aes.Create();
        aes.Key = flidKey;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using ICryptoTransform decryptor = aes.CreateDecryptor();
        byte[] decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);

        if (clearLength > 0 && clearLength <= decrypted.Length)
            return decrypted.AsSpan(0, clearLength).ToArray();

        return decrypted;
    }

    public static string ExtractSongIni(byte[] bytes)
    {
        try
        {
            var archive = ParseArchive(bytes);
            var songIni = archive.Entries.FirstOrDefault(e => e.Name.Equals("Song.ini", StringComparison.OrdinalIgnoreCase));
            if (songIni != null && songIni.Data.Length > 0)
                return DecodeIniBytes(songIni.Data);
        }
        catch
        {
            // Fallback níže drží kompatibilitu s případným divnějším KFN.
        }

        return ExtractSongIniBySearch(bytes);
    }

    private static string ExtractSongIniBySearch(byte[] bytes)
    {
        int songName = IndexOf(bytes, Encoding.ASCII.GetBytes("Song.ini"), 0);
        int general = IndexOf(bytes, Encoding.ASCII.GetBytes("[General]"), songName >= 0 ? songName : 0);

        if (general < 0)
            throw new InvalidDataException("V KFN nebyl nalezen čitelný Song.ini / [General].");

        int length = 0;
        if (songName >= 0 && songName + 8 < general)
        {
            for (int p = songName + "Song.ini".Length; p + 4 <= general; p++)
            {
                int candidate = BitConverter.ToInt32(bytes, p);
                if (candidate > 0 && general + candidate <= bytes.Length)
                    length = candidate;
            }
        }

        if (length <= 0 || general + length > bytes.Length)
            length = bytes.Length - general;

        byte[] iniBytes = bytes.Skip(general).Take(length).ToArray();
        return DecodeIniBytes(iniBytes);
    }

    public static bool TryReadEmbeddedAudio(string kfnPath, string sourceAudioPath, out string fileName, out byte[] audioBytes)
    {
        fileName = "";
        audioBytes = Array.Empty<byte>();

        KfnArchive archive;
        try
        {
            archive = ReadArchive(kfnPath);
        }
        catch
        {
            return false;
        }

        string wanted = FileNameOnly(sourceAudioPath);
        var audioEntries = archive.Entries
            .Where(e => IsAudioFileName(e.Name))
            .ToList();

        KfnArchiveEntry? chosen = null;

        // 1) Nejprve přesná shoda se Source=.
        if (!string.IsNullOrWhiteSpace(wanted))
            chosen = audioEntries.FirstOrDefault(e => FileNameOnly(e.Name).Equals(wanted, StringComparison.OrdinalIgnoreCase));

        // 2) Když se neshoduje název, ale KFN obsahuje přesně jedno audio,
        // je to prakticky jistě audio patřící k tomuto KFN. To extrahujeme.
        if (chosen == null && audioEntries.Count == 1)
            chosen = audioEntries[0];

        // 3) Když je v KFN víc audio souborů a žádný nesedí na Source=,
        // radši nehádáme. Pak nastoupí ruční dohledání.
        if (chosen == null || chosen.Data.Length == 0) return false;

        fileName = FileNameOnly(chosen.Name);
        audioBytes = chosen.Data;
        return true;
    }

    private static bool IsAudioFileName(string name)
    {
        string ext = Path.GetExtension(FileNameOnly(name));
        return ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".wav", StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeIniBytes(byte[] bytes)
    {
        // KFN není jednotné. Některé soubory mají celý Song.ini jako UTF-8,
        // jiné jako Windows-1250 a některé jsou dokonce míchané po řádcích:
        // Title/Text jsou UTF-8, ale Source/Track0 je CP1250.
        //
        // Proto od v12 nedetekujeme jedno kódování pro celý Song.ini,
        // ale vybíráme nejlepší dekódování pro každý řádek zvlášť.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var lines = new List<string>();
        int start = 0;

        for (int i = 0; i <= bytes.Length; i++)
        {
            if (i < bytes.Length && bytes[i] != (byte)'\n') continue;

            int len = i - start;
            if (len > 0 && bytes[start + len - 1] == (byte)'\r') len--;

            byte[] lineBytes = len > 0
                ? bytes.AsSpan(start, len).ToArray()
                : Array.Empty<byte>();

            lines.Add(DecodeIniLineBytes(lineBytes));
            start = i + 1;
        }

        return string.Join("\n", lines).TrimEnd('\0', '\r', '\n', ' ');
    }

    private static string DecodeIniLineBytes(byte[] bytes)
    {
        if (bytes.Length == 0) return "";

        var candidates = new List<(string Name, string Text)>();
        AddCandidate(candidates, "utf-8", new UTF8Encoding(false, false).GetString(bytes));
        AddCandidate(candidates, "windows-1250", Encoding.GetEncoding(1250).GetString(bytes));
        AddCandidate(candidates, "iso-8859-2", Encoding.GetEncoding(28592).GetString(bytes));
        AddCandidate(candidates, "cp852", Encoding.GetEncoding(852).GetString(bytes));

        return candidates
            .OrderByDescending(c => EncodingLineQualityScore(c.Text, c.Name))
            .First()
            .Text;
    }

    private static void AddCandidate(List<(string Name, string Text)> list, string name, string value)
    {
        if (!list.Any(x => x.Text == value)) list.Add((name, value));
    }

    private static int EncodingLineQualityScore(string s, string encodingName)
    {
        int score = 0;
        string line = s.Trim();

        if (line.Length == 0) return 0;

        if (line.StartsWith("[") && line.EndsWith("]")) score += 80;
        if (line.StartsWith("Text", StringComparison.OrdinalIgnoreCase)) score += 30;
        if (line.StartsWith("Sync", StringComparison.OrdinalIgnoreCase)) score += 30;
        if (line.StartsWith("Source=", StringComparison.OrdinalIgnoreCase)) score += 50;
        if (line.StartsWith("Track", StringComparison.OrdinalIgnoreCase)) score += 30;
        if (line.StartsWith("Title=", StringComparison.OrdinalIgnoreCase)) score += 30;
        if (line.StartsWith("Artist=", StringComparison.OrdinalIgnoreCase)) score += 30;
        if (line.StartsWith("KaraokeVersion=", StringComparison.OrdinalIgnoreCase)) score += 20;
        if (line.StartsWith("VocalGuide=", StringComparison.OrdinalIgnoreCase)) score += 20;
        if (line.StartsWith("KaraFunization=", StringComparison.OrdinalIgnoreCase)) score += 20;

        // Mírná preference běžných kódování pro české KaraFun exporty.
        if (encodingName == "windows-1250") score += 10;
        if (encodingName == "utf-8") score += 8;
        if (encodingName == "cp852") score -= 30;

        foreach (char c in s)
        {
            if ("áčďéěíňóřšťúůýžÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ".IndexOf(c) >= 0) score += 4;
            if (c == '\uFFFD') score -= 100;
            if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t') score -= 20;
        }

        string boxDrawing = "─━│┃┄┅┆┇┈┉┊┋┌┍┎┏┐┑┒┓└┕┖┗┘┙┚┛├┝┞┟┠┡┢┣┤┥┦┧┨┩┪┫┬┭┮┯┰┱┲┳┴┵┶┷┸┹┺┻┼┽┾┿╀╁╂╃╄╅╆╇╈╉╊╋═║╒╓╔╕╖╗╘╙╚╛╜╝╞╟╠╡╢╣╤╥╦╧╨╩╪╫╬";
        foreach (char c in s)
        {
            if (boxDrawing.IndexOf(c) >= 0) score -= 150;
        }

        // U českých KFN jsou tyto polské znaky skoro vždy stopa chybného CP852.
        foreach (char c in s)
        {
            if ("łŁśŚźŹżŻąĄęĘńŃ".IndexOf(c) >= 0) score -= 45;
        }

        int eq = line.IndexOf('=');
        if (eq >= 0)
        {
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();

            if (key.Equals("Title", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Artist", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Album", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Source", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("Track", StringComparison.OrdinalIgnoreCase)
                || TextKeyRegex.IsMatch(key))
            {
                score += ValueLooksReadableScore(value);
            }
        }

        string[] mojibake =
        {
            "Ă", "Ä", "Ĺ", "Å", "Ã", "Â", "�",
            "ÄŤ", "Ăˇ", "Ă©", "Ä›", "Ĺ™", "Ĺˇ", "Ĺľ", "ĹŻ",
            "Å¡", "Å¾", "Å™", "Å¯"
        };

        foreach (string bad in mojibake)
        {
            int idx = 0;
            while ((idx = s.IndexOf(bad, idx, StringComparison.Ordinal)) >= 0)
            {
                score -= 90;
                idx += bad.Length;
            }
        }

        return score;
    }

    private static int ValueLooksReadableScore(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;

        int score = 0;
        int letters = 0;
        int weird = 0;

        string allowedPunctuation = " .,;:!?()[]{}'\"+-*/&@#%°…–—_\\/:";

        foreach (char c in value)
        {
            if (char.IsLetter(c))
            {
                letters++;
                score += 2;
                continue;
            }

            if (char.IsDigit(c) || char.IsWhiteSpace(c) || allowedPunctuation.IndexOf(c) >= 0)
            {
                score += 1;
                continue;
            }

            weird++;
            score -= 25;
        }

        if (letters >= 3) score += 10;
        if (weird == 0) score += 15;

        return score;
    }

    private static string DecodeAnsi(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1250).GetString(bytes);
    }

    public static KfnSong ParseSongIni(string ini)
    {
        Dictionary<string, Dictionary<string, string>> sections = new(StringComparer.OrdinalIgnoreCase);
        string current = "";

        foreach (string raw in ini.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;

            Match sm = SectionRegex.Match(line);
            if (sm.Success)
            {
                current = sm.Groups[1].Value.Trim();
                if (!sections.ContainsKey(current)) sections[current] = new(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();
            if (!sections.ContainsKey(current)) sections[current] = new(StringComparer.OrdinalIgnoreCase);
            sections[current][key] = value;
        }

        var song = new KfnSong();
        if (sections.TryGetValue("General", out var general))
        {
            song.Title = Get(general, "Title");
            song.Artist = Get(general, "Artist");
            song.Album = Get(general, "Album");
            song.Source = Get(general, "Source");
        }

        // Některé KFN jsou jen audio obal bez karaoke textu.
        // Typicky mají v [Eff2] jen TextCount=0 a žádné Text0..TextN.
        // Starší verze to brala jako chybu, ale správně má vzniknout audio-only JSON.
        Dictionary<string, string>? textSection = sections.Values
            .Where(s => s.Keys.Any(k => TextKeyRegex.IsMatch(k)) || s.ContainsKey("TextCount"))
            .OrderByDescending(s => s.Keys.Count(k => TextKeyRegex.IsMatch(k)) + (s.ContainsKey("TextCount") ? 1 : 0))
            .FirstOrDefault();

        if (textSection == null)
        {
            song.Warnings.Add("Song.ini neobsahuje karaoke textovou sekci. Vytvořen audio-only JSON bez řádků textu.");
            return song;
        }

        int textCount = TryInt(Get(textSection, "TextCount"), -1);
        if (textCount < 0)
        {
            textCount = textSection.Keys
                .Select(k => TextKeyRegex.Match(k))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value))
                .DefaultIfEmpty(-1)
                .Max() + 1;
        }

        if (textCount <= 0)
        {
            song.Warnings.Add("TextCount=0. KFN neobsahuje karaoke text, vytvořen audio-only JSON.");
            return song;
        }

        for (int i = 0; i < textCount; i++)
        {
            song.TextLines.Add(Get(textSection, "Text" + i));
        }

        foreach (var pair in textSection)
        {
            Match m = SyncKeyRegex.Match(pair.Key);
            if (!m.Success) continue;
            foreach (string part in pair.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out int cs))
                {
                    // KaraFun/KFN Sync hodnoty jsou v setinách sekundy.
                    // JSON pro rádio/web používá milisekundy, proto převod *10.
                    song.SyncTimesMs.Add(checked(cs * 10));
                }
            }
        }

        if (song.SyncTimesMs.Count == 0)
            song.Warnings.Add("Nenalezeny Sync0..SyncN časy. Text půjde zobrazit jen bez přesného karaoke časování.");

        return song;
    }

    public static string CleanKaraokeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? "";

        // Vizuální text řádku:
        //   _ = mezera v textu
        //   / = dělení slabik v KFN, ve výstupu se NEZOBRAZÍ
        // Časování slabik se ale níže zachová přesně 1:1 proti Sync hodnotám.
        return text
            .Replace("_", " ")
            .Replace("/", "");
    }

    private sealed class KfnTimedPart
    {
        public string Text { get; set; } = "";
        public int CharStart { get; set; }
        public int CharLength { get; set; }
    }

    private static List<KfnTimedPart> SplitKfnTextToTimedParts(string text)
    {
        var parts = new List<KfnTimedPart>();

        if (string.IsNullOrWhiteSpace(text))
            return parts;

        // DŮLEŽITÉ: tady NESMÍME podtržítko převést na mezeru před dělením.
        // V KFN podtržítko znamená vizuální mezeru uvnitř jedné časované skupiny
        // (např. v_du/chu má sync části "v du" + "chu", ne "v" + "du" + "chu").
        string normalized = text;

        // Mapa indexů původního řádku na pozice ve vizuálním Textu.
        // Lomítko '/' není ve vizuálním Textu znak, proto neposouvá pozici.
        int[] displayPosAtIndex = new int[normalized.Length + 1];
        int displayPos = 0;

        for (int i = 0; i < normalized.Length; i++)
        {
            displayPosAtIndex[i] = displayPos;
            if (normalized[i] != '/')
                displayPos++;
        }
        displayPosAtIndex[normalized.Length] = displayPos;

        foreach (Match wordMatch in WordRegex.Matches(normalized))
        {
            string rawWord = wordMatch.Value;
            int rawBase = wordMatch.Index;
            int p = 0;

            while (p < rawWord.Length)
            {
                // Prázdné části po dvojitém nebo krajním lomítku nebereme jako časovanou slabiku,
                // stejně jako původní logika Split(... RemoveEmptyEntries).
                while (p < rawWord.Length && rawWord[p] == '/')
                    p++;

                int segmentStart = p;

                while (p < rawWord.Length && rawWord[p] != '/')
                    p++;

                if (segmentStart >= p)
                    continue;

                string partText = rawWord[segmentStart..p].Replace("_", " ");
                int sourceIndex = rawBase + segmentStart;

                parts.Add(new KfnTimedPart
                {
                    Text = partText,
                    CharStart = displayPosAtIndex[sourceIndex],
                    CharLength = partText.Length
                });
            }
        }

        return parts;
    }

    public static KaraokeDocument ToKaraokeDocument(KfnSong song, string sourceKfnPath, string audioFileName)
    {
        var doc = new KaraokeDocument
        {
            Title = string.IsNullOrWhiteSpace(song.Title) ? Path.GetFileNameWithoutExtension(sourceKfnPath) : song.Title,
            Artist = song.Artist,
            Album = song.Album,
            SourceKfn = Path.GetFileName(sourceKfnPath),
            Audio = audioFileName,
            OriginalAudioSource = ExtractAudioPath(song.Source),
            Warnings = new List<string>(song.Warnings)
        };

        int globalWordIndex = 0;
        int syncIndex = 0;
        bool hasWordSync = song.SyncTimesMs.Count > 0;

        for (int lineIndex = 0; lineIndex < song.TextLines.Count; lineIndex++)
        {
            string originalText = song.TextLines[lineIndex] ?? "";
            string displayText = CleanKaraokeText(originalText);
            var line = new KaraokeLine { Index = lineIndex, Text = displayText };

            // DŮLEŽITÉ:
            // KFN Sync hodnoty necháváme přesně 1:1 pro časované části/slabiky.
            // Nic neslučujeme, nic neodhazujeme a žádné časy nepřeskakujeme heuristikou.
            // Player má zobrazit line.Text a podle CharStart/CharLength zvýrazňovat části.
            List<KfnTimedPart> timedParts = SplitKfnTextToTimedParts(originalText);

            foreach (KfnTimedPart timedPart in timedParts)
            {
                int t = 0;
                if (hasWordSync)
                {
                    if (syncIndex < song.SyncTimesMs.Count) t = song.SyncTimesMs[syncIndex];
                    else t = song.SyncTimesMs.LastOrDefault() + 800;
                    syncIndex++;
                }

                line.Words.Add(new KaraokeWord
                {
                    Index = globalWordIndex++,
                    Text = timedPart.Text,
                    TimeMs = t,
                    EndMs = t + 700,
                    CharStart = timedPart.CharStart,
                    CharLength = timedPart.CharLength
                });
            }

            if (line.Words.Count > 0)
            {
                line.StartMs = line.Words[0].TimeMs;
                line.EndMs = line.Words[^1].TimeMs + 1000;
            }
            else
            {
                line.StartMs = doc.Lines.LastOrDefault()?.EndMs ?? 0;
                line.EndMs = line.StartMs + 1000;
            }

            doc.Lines.Add(line);
        }

        var allWords = doc.Lines.SelectMany(l => l.Words).ToList();
        for (int i = 0; i < allWords.Count; i++)
        {
            int next = i + 1 < allWords.Count ? allWords[i + 1].TimeMs : allWords[i].TimeMs + 900;
            allWords[i].EndMs = Math.Max(allWords[i].TimeMs + 120, next - 20);
        }

        for (int i = 0; i < doc.Lines.Count; i++)
        {
            var line = doc.Lines[i];
            if (line.Words.Count == 0) continue;

            int nextLineStart = doc.Lines.Skip(i + 1).FirstOrDefault(l => l.Words.Count > 0)?.StartMs ?? (line.Words[^1].EndMs + 1000);
            line.EndMs = Math.Max(line.Words[^1].EndMs + 200, nextLineStart - 80);
        }

        if (hasWordSync && syncIndex != song.SyncTimesMs.Count)
            doc.Warnings.Add($"Počet sync časů ({song.SyncTimesMs.Count}) nesedí na počet časovaných částí/slabik ({syncIndex}). Syncy se nevyhazují, jen jich v KFN a textu není stejný počet.");

        return doc;
    }

    public static string ExtractAudioPath(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "";
        string[] parts = source.Split(',', 3);
        return parts.Length == 3 ? parts[2].Trim() : source.Trim();
    }

    public static string FileNameOnly(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string normalized = path.Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

    public static string SafeFileBase(string value)
    {
        string name = Path.GetFileNameWithoutExtension(value);
        if (string.IsNullOrWhiteSpace(name)) name = "karaoke";
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        return name.Trim();
    }

    private static uint ReadUInt32(byte[] bytes, ref int pos)
    {
        if (pos + 4 > bytes.Length) throw new InvalidDataException("Neúplný LE32 záznam.");
        uint value = BitConverter.ToUInt32(bytes, pos);
        pos += 4;
        return value;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        if (needle.Length == 0) return 0;
        for (int i = Math.Max(0, start); i <= haystack.Length - needle.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { ok = false; break; }
            }
            if (ok) return i;
        }
        return -1;
    }

    private static string Get(Dictionary<string, string> data, string key) => data.TryGetValue(key, out string? value) ? value : "";
    private static int TryInt(string value, int fallback) => int.TryParse(value, out int i) ? i : fallback;
}
