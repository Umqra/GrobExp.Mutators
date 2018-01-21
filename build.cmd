@echo off
set dotNetBasePath=%windir%\Microsoft.NET\Framework
if exist %dotNetBasePath%64 set dotNetBasePath=%dotNetBasePath%64
for /R %dotNetBasePath% %%i in (*msbuild.exe) do set msbuild=%%i

cd GrobExp
..\nuget.exe restore
%msbuild% /v:q /t:Rebuild /p:Configuration=Release /nodeReuse:false /maxcpucount GrobExp.sln

pause
