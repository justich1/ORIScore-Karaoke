# ORIScore Karaoke

Free karaoke toolkit for converting, editing and playing karaoke songs in the ORIScore `.ock` format.

ORIScore Karaoke is a small set of tools for working with karaoke files.
The goal is to have a simple open format, offline playback, web playback and support for embedded/converted karaoke timing.

## What is included

This project contains several parts of the ORIScore Karaoke ecosystem:

* **Karaoke converter**
  Converts karaoke sources into the ORIScore karaoke format.

* **Karaoke editor**
  Allows editing and timing karaoke lyrics.

* **Karaoke player**
  Plays karaoke songs with synchronized lyrics.

* **HTML / Web player**
  Browser-based player for `.ock` and legacy `.karaoke.json` files.

* **ESP32 / INO player**
  ESP32-S3 based karaoke/audio player with web interface, USB/FFat storage support and `.ock` playback.

## ORIScore `.ock` format

The `.ock` file is the main ORIScore Karaoke file format.

It is a JSON-based karaoke file containing:

* song title
* artist
* audio file reference
* timed lines
* timed words or syllables

Example structure:

```json
{
  "Format": "ORIS_KARAOKE_V1",
  "Title": "Song title",
  "Artist": "Artist name",
  "Audio": "zaklad/song.mp3",
  "Lines": [
    {
      "Index": 0,
      "Text": "Chvilku vzpomínej",
      "StartMs": 1200,
      "EndMs": 3500,
      "Words": [
        {
          "Text": "Chvil",
          "TimeMs": 1200
        },
        {
          "Text": "ku",
          "TimeMs": 1450
        },
        {
          "Text": "vzpo",
          "TimeMs": 1900
        },
        {
          "Text": "mí",
          "TimeMs": 2200
        },
        {
          "Text": "nej",
          "TimeMs": 2500
        }
      ]
    }
  ]
}
```

## Syllable rendering

ORIScore Karaoke supports syllable-based timing.

In many converted karaoke files the `Words` array does not contain full words, but timed syllables.
For example:

```text
Chvil + ku + vzpo + mí + nej
```

is displayed as:

```text
Chvilku vzpomínej
```

The player uses the full line text from `Text` for spacing and uses `Words` only for timing/highlighting.
This prevents bad rendering where every syllable would be separated by a space.

Slashes from older KFN-style syllable splitting are treated as syllable separators and are not displayed.

Example:

```text
Chvil/ku vzpo/mí/nej
```

is displayed as:

```text
Chvilku vzpomínej
```

## Supported file extensions

Main supported karaoke files:

```text
.ock
.karaoke.json
```

Recommended format for new files:

```text
.ock
```

Supported audio files depend on the player version, but commonly include:

```text
.mp3
.wav
```

The HTML player can also search for common browser-supported audio formats when available.

## HTML / Web player

The web player is designed for local offline use in a browser.

Features:

* opens a folder with karaoke files
* supports `.ock`
* supports legacy `.karaoke.json`
* searches for audio next to the karaoke file
* supports subfolders
* displays previous, current and next line
* highlights syllables according to timing
* fullscreen mode
* manual previous / next song buttons
* adjustable timing offset

### Recommended start

Use the included launcher if available:

```text
START_PLAYER.bat
```

Then open:

```text
http://localhost:8899/
```

Opening through a local server is better than opening `index.html` directly, because browsers can block some local file access when using `file://`.

## ESP32 / INO player

The ESP32 version is intended for standalone playback from USB or internal FFat storage.

Main features:

* ESP32-S3 support
* USB storage support
* internal FFat storage support
* web interface
* karaoke page
* `.ock` playback
* legacy `.karaoke.json` playback
* synchronized lyrics rendered in browser
* audio playback handled by ESP32
* optional I2S DAC output
* rotary encoder support for volume/control depending on firmware version

The karaoke web page scans storage recursively and lists supported karaoke files.

Supported karaoke extensions in the ESP32 version:

```text
.ock
.karaoke.json
```

## Audio file lookup

Players try to find the matching audio file using several methods:

1. Audio path from the karaoke file.
2. Audio file in the same folder.
3. Audio file with the same base name as the karaoke file.
4. Normalized filename matching where supported.

Example:

```text
Slzy tvy mamy.ock
Slzy tvy mamy.mp3
```

or:

```text
Slzy tvy mamy.ock
zaklad/Slzy tvy mamy.mp3
```

## Recommended folder layout

```text
karaoke/
├── Slzy tvy mamy.ock
├── zaklad/
│   └── Slzy tvy mamy.mp3
├── song-2.ock
└── song-2.mp3
```

or simply:

```text
karaoke/
├── Slzy tvy mamy.ock
├── Slzy tvy mamy.mp3
├── Another song.ock
└── Another song.mp3
```

## Project goal

The goal of ORIScore Karaoke is to provide a free and simple karaoke system that can run:

* on Windows
* in a browser
* on ESP32 hardware
* offline without cloud services

The project is focused on practical use, simple file formats and easy conversion from older karaoke formats.

## Status

This project is under active development.

Current focus:

* `.ock` support
* better syllable rendering
* HTML player improvements
* ESP32 standalone playback
* editor/player workflow
* converter improvements

## License

License is not specified yet.

If you plan to publish this as open source, add a license file such as:

```text
MIT
GPL-3.0
Apache-2.0
```

## Author

Created by ORIScore / oris-core.cz.

Repository:

```text
https://github.com/justich1/ORIScore-Karaoke
```
