@echo off
rem Builds dim.exe with the C# compiler that ships with Windows. No SDK, no NuGet, no install.
rem /target:winexe means the process never gets a console window.
cd /d "%~dp0"
set CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /out:dim.exe ^
  /reference:System.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  dim.cs

