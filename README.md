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

### Doporučené spuštění

Pokud je součástí balíčku spouštěč:

```text
START_PLAYER.bat
```

spusť jej a otevři v prohlížeči:

```text
http://localhost:8899/
```

Spuštění přes lokální server je vhodnější než přímé otevření `index.html`, protože prohlížeče často omezují přístup k lokálním souborům při použití `file://`.

## ESP32 / INO přehrávač

ESP32 verze je určená pro samostatné přehrávání z USB nebo interního FFat úložiště.

Hlavní funkce:

* podpora ESP32-S3
* podpora USB úložiště
* podpora interního FFat úložiště
* webové rozhraní
* karaoke stránka
* přehrávání `.ock`
* přehrávání staršího `.karaoke.json`
* synchronizované texty v prohlížeči
* přehrávání audia přes ESP32
* možnost výstupu přes I2S DAC
* možnost ovládání rotačním enkodérem podle verze firmware

Karaoke stránka umí prohledat úložiště i v podsložkách a vypsat podporované karaoke soubory.

Podporované karaoke přípony v ESP32 verzi:

```text
.ock
.karaoke.json
```

## Vyhledávání audio souboru

Přehrávače se snaží najít odpovídající audio několika způsoby:

1. Podle cesty uvedené v karaoke souboru.
2. Ve stejné složce jako karaoke soubor.
3. Podle stejného názvu souboru.
4. Podle normalizovaného názvu, pokud to daný přehrávač podporuje.

Příklad:

```text
Slzy tvy mamy.ock
Slzy tvy mamy.mp3
```

nebo:

```text
Slzy tvy mamy.ock
zaklad/Slzy tvy mamy.mp3
```

## Doporučená struktura složek

```text
karaoke/
├── Slzy tvy mamy.ock
├── zaklad/
│   └── Slzy tvy mamy.mp3
├── dalsi-skladba.ock
└── dalsi-skladba.mp3
```

nebo jednoduše:

```text
karaoke/
├── Slzy tvy mamy.ock
├── Slzy tvy mamy.mp3
├── Dalsi skladba.ock
└── Dalsi skladba.mp3
```

## Cíl projektu

Cílem ORIScore Karaoke je vytvořit jednoduchý bezplatný karaoke systém, který může běžet:

* ve Windows
* ve webovém prohlížeči
* na ESP32 hardwaru
* offline bez cloudových služeb

Projekt je zaměřený na praktické použití, jednoduchý formát souborů a snadný převod ze starších karaoke formátů.

## Stav projektu

Projekt je ve vývoji.

Aktuálně se řeší hlavně:

* podpora `.ock`
* lepší zobrazování slabik
* HTML přehrávač
* ESP32 samostatné přehrávání
* editor a přehrávač pro Windows
* konvertor karaoke souborů

## Licence

Licence zatím není určena.

Pokud má být projekt veřejně open-source, je vhodné doplnit například:

```text
MIT
GPL-3.0
Apache-2.0
```

## Autor

Vytvořeno pod projektem ORIScore / oris-core.cz.

Repozitář:

```text
https://github.com/justich1/ORIScore-Karaoke
```
