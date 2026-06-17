@echo off
reg delete "HKCU\Software\Classes\.ock" /f
reg delete "HKCU\Software\Classes\.karaoke" /f
reg delete "HKCU\Software\Classes\ORIScoreKaraokeFile" /f
echo Asociace .ock a .karaoke odstranena pro aktualniho uzivatele.
pause
