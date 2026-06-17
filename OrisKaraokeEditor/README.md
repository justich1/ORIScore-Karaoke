# ORIS Karaoke Tap Editor WPF v2

Editor pro rychlé časování karaoke textu mezerníkem.

## Novinky v2

- Podpora vlastní koncovky `.ock`.
- Otevře `.ock`, `.karaoke.json` i `.json`.
- Ukládání jako `.ock` je výchozí.
- Umí otevřít soubor předaný jako argument:
  ```text
  OrisKaraokeEditorWpf.exe "skladba.ock"
  ```

## Princip

1. Vložíš celý text skladby.
2. Klikneš **Vytvořit slova z textu**.
3. Vybereš audio.
4. Spustíš písničku.
5. Mačkáš **mezerník** v rytmu slov.
6. Editor zapisuje `TimeMs` pro další slovo.
7. Uložíš `.ock`.

## Ovládání

- Enter: play/pause
- Space: zapsat čas dalšího slova
- Backspace: krok o slovo zpět
