@echo off
rem ======================================================================
rem  Installe (ou met a jour) le Lecteur vocal sur ce PC.
rem  Double-clic sur ce fichier, a refaire apres chaque modification du code.
rem ======================================================================
rem  chcp 65001 : la console affiche correctement les accents (UTF-8).
chcp 65001 > nul

rem  Dossier d'installation : AppData\Local\Programs, l'emplacement standard
rem  des applications installées pour un seul utilisateur (pas besoin d'être administrateur).
set "DESTINATION=%LOCALAPPDATA%\Programs\LecteurVocal"

rem  dotnet n'est pas toujours dans le PATH d'une console ouverte avant son installation.
set "DOTNET=dotnet"
where dotnet > nul 2>&1 || set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"

echo Fermeture du Lecteur vocal s'il est ouvert...
taskkill /IM LecteurVocal.exe /F > nul 2>&1

echo Compilation de la version finale...
rem  %~dp0 = dossier de ce fichier .cmd.
rem  -c Release : version optimisée.  PublishSingleFile : un seul .exe.
rem  --self-contained false : utilise le .NET déjà installé sur le PC (exe léger).
"%DOTNET%" publish "%~dp0LecteurVocal.csproj" -c Release -r win-x64 --self-contained false ^
    -p:PublishSingleFile=true -p:DebugType=none -o "%DESTINATION%" -nologo -v q
if errorlevel 1 (
    echo.
    echo ERREUR pendant la compilation.
    pause
    exit /b 1
)

echo Création du raccourci dans le menu Démarrer...
powershell -NoProfile -Command "$r = (New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Programs') + '\Lecteur vocal.lnk'); $r.TargetPath = '%DESTINATION%\LecteurVocal.exe'; $r.Description = 'Lecture à voix haute du texte sélectionné (touche Pause)'; $r.Save()"

rem  Si le lancement au démarrage de Windows est activé, on le fait pointer vers la version installée.
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v LecteurVocal > nul 2>&1 && ^
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v LecteurVocal /d "\"%DESTINATION%\LecteurVocal.exe\" --demarrage" /f > nul

echo Lancement...
start "" "%DESTINATION%\LecteurVocal.exe"

echo.
echo Terminé ! Le Lecteur vocal est dans le menu Démarrer.
timeout /t 4 > nul 2>&1
