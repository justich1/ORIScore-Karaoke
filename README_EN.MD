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

Opening through a local server is better than opening `index.html` directly, because browsers can block some local file access when using `file://`.

## License

```text
MIT
```
