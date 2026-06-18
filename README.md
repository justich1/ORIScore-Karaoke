# ORIScore Karaoke

Bezplatná sada nástrojů pro převod, editaci a přehrávání karaoke skladeb ve formátu ORIScore `.ock`.

ORIScore Karaoke je jednoduchý karaoke systém, který umí pracovat s časovaným textem, slabikami a audio soubory.
Cílem je mít otevřený a snadno čitelný formát, který funguje offline, ve webovém prohlížeči, ve Windows aplikacích i na ESP32 zařízení.

## Co projekt obsahuje

Projekt obsahuje několik částí:

* **Karaoke konvertor**
  Převod karaoke souborů do formátu ORIScore Karaoke.

* **Karaoke editor**
  Úprava textů a časování karaoke skladeb.

* **Karaoke přehrávač**
  Přehrávání karaoke skladeb se synchronizovaným textem.

* **HTML / webový přehrávač**
  Přehrávač běžící v prohlížeči s podporou `.ock` a staršího `.karaoke.json`.

* **ESP32 / INO přehrávač**
  Samostatný karaoke/audio přehrávač pro ESP32-S3 s webovým rozhraním.

## Formát ORIScore `.ock`

Hlavním formátem projektu je `.ock`.

Jedná se o JSON soubor, který obsahuje:

* název skladby
* interpreta
* cestu k audio souboru
* časované řádky textu
* časovaná slova nebo slabiky

Ukázka struktury:

```json
{
  "Format": "ORIS_KARAOKE_V1",
  "Title": "Název skladby",
  "Artist": "Interpret",
  "Audio": "zaklad/skladba.mp3",
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

## Slabikování

ORIScore Karaoke podporuje časování po slabikách.

V některých převedených karaoke souborech pole `Words` neobsahuje celá slova, ale jednotlivé slabiky.
Například:

```text
Chvil + ku + vzpo + mí + nej
```

se zobrazí jako:

```text
Chvilku vzpomínej
```

Přehrávač používá celé znění řádku z položky `Text` a položky ve `Words` používá hlavně pro časování a zvýrazňování.
Díky tomu se mezi každou slabiku nevkládá zbytečná mezera.

Starší zápis slabik přes lomítka se při zobrazení čistí.

Například:

```text
Chvil/ku vzpo/mí/nej
```

se zobrazí jako:

```text
Chvilku vzpomínej
```

## Podporované přípony

Hlavní podporované karaoke soubory:

```text
.ock
.karaoke.json
```

Doporučený formát pro nové soubory:

```text
.ock
```

Podporované audio soubory se mohou lišit podle konkrétního přehrávače, běžně se ale počítá s:

```text
.mp3
.wav
```

## HTML / webový přehrávač

Webový přehrávač je určený pro lokální offline použití v prohlížeči.

Umí:

* otevřít složku s karaoke soubory
* načítat `.ock`
* načítat starší `.karaoke.json`
* vyhledat audio ke karaoke souboru
* pracovat s podsložkami
* zobrazovat předchozí, aktuální a další řádek
* zvýrazňovat text podle časování
* pracovat se slabikami
* fullscreen režim
* ruční přepínání skladeb
* korekci časování pomocí offsetu

Spuštění přes lokální server je vhodnější než přímé otevření `index.html`, protože prohlížeče často omezují přístup k lokálním souborům při použití `file://`.

## Licence

```text
MIT
```
