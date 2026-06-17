# ORIS Karaoke Web Player v2

## Doporučené spuštění ve Windows

Spusť:

```text
START_PLAYER.bat
```

Otevře se:

```text
http://localhost:8899/
```

Tohle je lepší než otevírat `index.html` přímo jako `file://`, protože Chrome u lokálních souborů některé věci blokuje.

## Použití

1. Klikni **Otevřít složku**.
2. Vyber složku, kde máš `.karaoke.json` a audio soubory.
3. Vyber skladbu vlevo.

## Opravy ve v2

- Přidaný `START_PLAYER.bat`.
- Lepší hledání audia:
  - bere `Audio` i `audio`,
  - umí vyhodit prefix z KFN `1,L,`,
  - hledá podle celé cesty,
  - hledá podle názvu souboru,
  - hledá podle normalizovaného názvu bez diakritiky, mezer a číslování,
  - má fuzzy fallback.
- Ukazuje počet souborů bez nalezeného audia.


## Změna ve v3

- Po dohrání skladby se další skladba nespustí automaticky.
- Tlačítka předchozí/další zůstávají funkční ručně.
