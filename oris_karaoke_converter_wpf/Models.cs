namespace OrisKaraokeConverter;

public sealed class KaraokeDocument
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
    public List<string> Warnings { get; set; } = new();
}

public sealed class KaraokeLine
{
    public int Index { get; set; }
    public string Text { get; set; } = "";
    public int StartMs { get; set; }
    public int EndMs { get; set; }
    public List<KaraokeWord> Words { get; set; } = new();
}

public sealed class KaraokeWord
{
    public int Index { get; set; }
    public string Text { get; set; } = "";
    public int TimeMs { get; set; }
    public int EndMs { get; set; }

    // Pozice časované části/slabiky ve vizuálním KaraokeLine.Text.
    // Díky tomu může player zobrazit celý řádek normálně bez lomítek
    // a přitom zvýrazňovat přesné slabikové časování z KFN.
    public int CharStart { get; set; }
    public int CharLength { get; set; }
}

public sealed class KfnSong
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Source { get; set; } = "";
    public List<string> TextLines { get; set; } = new();
    public List<int> SyncTimesMs { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class ConvertOptions
{
    public bool Recursive { get; set; } = true;
    public bool CopyAudio { get; set; } = true;
    public bool Overwrite { get; set; } = true;
    public bool PreserveFolders { get; set; } = false;
    public bool CreateIndexJson { get; set; } = true;
    public bool PromptForMissingAudio { get; set; } = true;

    // kfnPath, originalAudioPath -> selectedAudioPath or null
    public Func<string, string, string?>? MissingAudioResolver { get; set; }
}

public sealed class ConvertResult
{
    public string KfnPath { get; set; } = "";
    public string OutputJson { get; set; } = "";
    public string AudioPath { get; set; } = "";
    public bool AudioFound { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}
