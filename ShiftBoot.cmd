@echo off
title ShiftBoot
SET MSBUILDENABLEALLPROPERTYFUNCTIONS=1
%WinDir%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe %~dp0\Common\BootTasks.proj /t:ShiftBoot /tv:4.0
pause
