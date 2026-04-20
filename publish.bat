@echo off

set OutputDir=Butubukuai_Release_v1.0

echo Clearing old build directories...
if exist %OutputDir% rmdir /s /q %OutputDir%
if exist "%OutputDir%.zip" del /f /q "%OutputDir%.zip"

echo [1/3] Building and publishing the executable...
dotnet publish Butubukuai/Butubukuai.csproj -c Release -r win-x64 --self-contained false -o %OutputDir%

echo [2/3] Copying external assets and configs...
xcopy "Butubukuai\bin\Debug\net9.0-windows\HelpImages" "%OutputDir%\HelpImages\" /E /I /Y >nul
copy "Butubukuai\appconfig.example.json" "%OutputDir%\" >nul
copy "README.md" "%OutputDir%\" >nul

echo [3/3] Compressing to ZIP package...
powershell -NoProfile -Command "Compress-Archive -Path '.\%OutputDir%\*' -DestinationPath '.\%OutputDir%.zip' -Force"

echo ==============================================================
echo Publish and Packaging completed successfully!
echo Output Directory: %OutputDir%
echo Output ZIP: %OutputDir%.zip
echo ==============================================================
pause
