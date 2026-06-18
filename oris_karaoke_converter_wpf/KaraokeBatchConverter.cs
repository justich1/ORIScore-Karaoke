using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrisKaraokeConverter;

public static class KaraokeBatchConverter
{
    private static readonly string[] AudioExtensions = new[] { ".mp3", ".wav" };

    public static List<string> FindKfnFiles(string inputDir, bool recursive)
    {
        if (!Directory.Exists(inputDir)) return [];

        return Directory.GetFiles(
                inputDir,
                "*.kfn",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly
            )
            .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static ConvertResult ConvertOne(string kfnPath, string inputRoot, string outputRoot, ConvertOptions options)
    {
        var result = new ConvertResult { KfnPath = kfnPath };

        try
        {
            string relativeDir = "";

            if (options.PreserveFolders && !string.IsNullOrWhiteSpace(inputRoot))
            {
                string dir = Path.GetDirectoryName(kfnPath) ?? "";
                relativeDir = Path.GetRelativePath(inputRoot, dir);

                if (relativeDir == ".")
                    relativeDir = "";
            }

            string targetDir = Path.Combine(outputRoot, relativeDir);
            string baseName = KfnParser.SafeFileBase(kfnPath);

            // ORIScore karaoke soubor.
            // DŮLEŽITÉ: výstupní cestu spočítáme hned na začátku.
            // Když je overwrite vypnutý a .ock už existuje, celý KFN přeskočíme
            // ještě před parsováním KFN, hledáním audia nebo dotazem na ruční výběr.
            string jsonName = baseName + ".ock";
            string jsonPath = Path.Combine(targetDir, jsonName);

            result.OutputJson = jsonPath;

            if (!options.Overwrite && File.Exists(jsonPath))
            {
                result.Success = true;
                result.AudioFound = true;
                result.AudioPath = "";
                result.Message = "Přeskočeno, výstup už existuje";
                return result;
            }

            Directory.CreateDirectory(targetDir);

            KfnSong song = KfnParser.ParseFile(kfnPath);

            string sourceAudioPath = KfnParser.ExtractAudioPath(song.Source);
            string sourceAudioName = KfnParser.FileNameOnly(sourceAudioPath);
            bool sourceSaysEmbedded = IsEmbeddedSource(song.Source);

            string? foundAudio = sourceSaysEmbedded ? null : FindAudioForKfn(kfnPath, sourceAudioPath);
            bool manualAudioSelected = false;

            bool embeddedAudioFound = KfnParser.TryReadEmbeddedAudio(
                kfnPath,
                sourceAudioPath,
                out string embeddedAudioName,
                out byte[] embeddedAudioBytes
            );

            if (foundAudio == null
                && !embeddedAudioFound
                && options.PromptForMissingAudio
                && options.MissingAudioResolver != null)
            {
                string? selected = options.MissingAudioResolver(kfnPath, sourceAudioPath);

                if (!string.IsNullOrWhiteSpace(selected)
                    && File.Exists(selected)
                    && IsAudioFile(selected))
                {
                    foundAudio = selected;
                    manualAudioSelected = true;
                }
            }

            result.AudioFound = foundAudio != null || embeddedAudioFound;
            result.AudioPath = foundAudio
                ?? (embeddedAudioFound ? "vložené v KFN: " + embeddedAudioName : sourceAudioPath);

            // Audio se ukládá do podsložky "zaklad" vedle vytvořeného .ock souboru.
            // V JSONu je proto relativní webová cesta zaklad/nazev.mp3.
            string audioSubDirName = "zaklad";
            string targetAudioDir = Path.Combine(targetDir, audioSubDirName);

            string audioFileName = !string.IsNullOrWhiteSpace(sourceAudioName)
                ? sourceAudioName
                : baseName + ".mp3";

            if (foundAudio != null)
            {
                audioFileName = Path.GetFileName(foundAudio);

                if (options.CopyAudio)
                {
                    Directory.CreateDirectory(targetAudioDir);
                    string targetAudio = Path.Combine(targetAudioDir, audioFileName);

                    if (options.Overwrite || !File.Exists(targetAudio))
                        File.Copy(foundAudio, targetAudio, true);
                }
            }
            else if (embeddedAudioFound)
            {
                audioFileName = embeddedAudioName;

                if (options.CopyAudio)
                {
                    Directory.CreateDirectory(targetAudioDir);
                    string targetAudio = Path.Combine(targetAudioDir, audioFileName);

                    if (options.Overwrite || !File.Exists(targetAudio))
                        File.WriteAllBytes(targetAudio, embeddedAudioBytes);
                }
            }

            string audioPathForJson = ToWebPath(Path.Combine(audioSubDirName, audioFileName));
            KaraokeDocument doc = KfnParser.ToKaraokeDocument(song, kfnPath, audioPathForJson);

            if (result.AudioFound)
            {
                result.AudioPath = Path.Combine(targetAudioDir, audioFileName);
            }

            if (!result.AudioFound)
            {
                doc.Warnings.Add(
                    "Audio nebylo nalezeno při konverzi. JSON ukazuje do podsložky zaklad, audio musíš dodat ručně tam."
                );
            }
            else if (embeddedAudioFound && foundAudio == null)
            {
                doc.Warnings.Add(
                    "Audio bylo vytaženo přímo z vloženého MP3/WAV souboru uvnitř KFN."
                );
            }
            else if (manualAudioSelected)
            {
                doc.Warnings.Add(
                    "Audio bylo ručně dohledáno při konverzi, protože původní Source cesta neexistovala."
                );
            }

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            File.WriteAllText(
                jsonPath,
                JsonSerializer.Serialize(doc, jsonOptions),
                new UTF8Encoding(false)
            );

            result.Success = true;

            if (doc.Lines.Count == 0)
            {
                result.Message = result.AudioFound
                    ? (manualAudioSelected ? "OK, bez textu, audio dohledáno" : "OK, bez textu")
                    : "OK, bez textu a chybí audio";
            }
            else
            {
                result.Message = result.AudioFound
                    ? (manualAudioSelected ? "OK, audio dohledáno" : "OK")
                    : "OK, ale chybí audio";
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = ex.Message;
            return result;
        }
    }

    public static void CreateIndexJson(string outputRoot)
    {
        if (!Directory.Exists(outputRoot))
            return;

        var items = Directory.GetFiles(outputRoot, "*.*", SearchOption.AllDirectories)
            .Where(IsKaraokeOutputFile)
            .OrderBy(p => p, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new
            {
                path = ToWebPath(Path.GetRelativePath(outputRoot, p)),
                name = KaraokeDisplayName(p)
            })
            .ToList();

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        File.WriteAllText(
            Path.Combine(outputRoot, "karaoke.index.json"),
            JsonSerializer.Serialize(items, jsonOptions),
            new UTF8Encoding(false)
        );
    }

    private static bool IsKaraokeOutputFile(string path)
    {
        string name = Path.GetFileName(path);

        return name.EndsWith(".ock", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".karaoke.json", StringComparison.OrdinalIgnoreCase);
    }

    private static string KaraokeDisplayName(string path)
    {
        string name = Path.GetFileName(path);

        if (name.EndsWith(".karaoke.json", StringComparison.OrdinalIgnoreCase))
            return name[..^".karaoke.json".Length];

        if (name.EndsWith(".ock", StringComparison.OrdinalIgnoreCase))
            return name[..^".ock".Length];

        return Path.GetFileNameWithoutExtension(name);
    }

    private static string ToWebPath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static bool IsEmbeddedSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        string[] parts = source.Split(',', 3);

        return parts.Length >= 2
            && parts[1].Trim().Equals("I", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindAudioForKfn(string kfnPath, string sourceAudioPath)
    {
        string kfnDir = Path.GetDirectoryName(kfnPath) ?? "";

        // DŮLEŽITÉ:
        // Starší verze tady jako poslední fallback brala "nejpodobnější" MP3/WAV ze složky.
        // To je nebezpečné, protože u některých KFN pak JSON ukazoval na úplně cizí skladbu.
        // Od v7 nehádáme. Buď najdeme přesný Source soubor, přesný soubor vedle KFN,
        // nebo audio označíme jako chybějící.

        if (!string.IsNullOrWhiteSpace(sourceAudioPath))
        {
            // 1) Přesná cesta ze Source=
            if (File.Exists(sourceAudioPath))
                return sourceAudioPath;

            string sourceName = KfnParser.FileNameOnly(sourceAudioPath);

            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                // 2) Přesný název audia vedle KFN
                string sameDir = Path.Combine(kfnDir, sourceName);

                if (File.Exists(sameDir))
                    return sameDir;

                // 3) Přesný název bez ohledu na velikost písmen ve stejné složce
                string? exactCaseInsensitive = Directory.GetFiles(
                        kfnDir,
                        "*.*",
                        SearchOption.TopDirectoryOnly
                    )
                    .FirstOrDefault(p =>
                        IsAudioFile(p)
                        && Path.GetFileName(p).Equals(sourceName, StringComparison.OrdinalIgnoreCase)
                    );

                if (exactCaseInsensitive != null)
                    return exactCaseInsensitive;
            }
        }

        // 4) Jen pokud Source nemá použitelný název, zkus přesně název KFN + .mp3/.wav.
        // Pokud Source název má, ale soubor chybí, nebudeme hádat podle názvu KFN.
        if (string.IsNullOrWhiteSpace(KfnParser.FileNameOnly(sourceAudioPath)))
        {
            string baseName = Path.GetFileNameWithoutExtension(kfnPath);

            foreach (string ext in AudioExtensions)
            {
                string p = Path.Combine(kfnDir, baseName + ext);

                if (File.Exists(p))
                    return p;

                string? exactBaseCaseInsensitive = Directory.GetFiles(
                        kfnDir,
                        "*.*",
                        SearchOption.TopDirectoryOnly
                    )
                    .FirstOrDefault(x =>
                        IsAudioFile(x)
                        && Path.GetFileName(x).Equals(baseName + ext, StringComparison.OrdinalIgnoreCase)
                    );

                if (exactBaseCaseInsensitive != null)
                    return exactBaseCaseInsensitive;
            }
        }

        return null;
    }

    private static bool IsAudioFile(string path)
    {
        string ext = Path.GetExtension(path);

        return AudioExtensions.Any(a =>
            a.Equals(ext, StringComparison.OrdinalIgnoreCase)
        );
    }
}