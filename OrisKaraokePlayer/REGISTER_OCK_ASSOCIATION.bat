@echo off
setlocal
cd /d "%~dp0"

set "EXE=%~dp0OrisKaraokePlayerWpf.exe"

if not exist "%EXE%" (
  echo Nenalezeno: %EXE%
  echo.
  echo Tenhle BAT spust az ve slozce s hotovym EXE po buildu/publish.
  pause
  exit /b 1
)

echo Registruji .ock a .karaoke pro aktualniho uzivatele...

reg add "HKCU\Software\Classes\.ock" /ve /d "ORIScoreKaraokeFile" /f
reg add "HKCU\Software\Classes\.karaoke" /ve /d "ORIScoreKaraokeFile" /f
reg add "HKCU\Software\Classes\ORIScoreKaraokeFile" /ve /d "ORIScore Karaoke" /f
reg add "HKCU\Software\Classes\ORIScoreKaraokeFile\DefaultIcon" /ve /d "\"%EXE%\",0" /f
reg add "HKCU\Software\Classes\ORIScoreKaraokeFile\shell\open\command" /ve /d "\"%EXE%\" \"%%1\"" /f

echo.
echo Hotovo. Soubor .ock i .karaoke by se mel otevirat v ORIS Karaoke Playeru.
echo Single-instance rezim: dalsi soubor se posle do beziciho playeru.
echo Kdyz se to neprojevi hned, odhlas/prihlas Windows nebo restartuj Explorer.
pause
