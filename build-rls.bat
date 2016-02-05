: build Andl release
setlocal
set rlsdir=temp-andl-rls

rd %rlsdir% /s
if exist %rlsdir% echo "needs manual delete"
if exist %rlsdir% exit /b

git clone Andl %rlsdir% -b master
cd %rlsdir%

c:\sw\nuget restore Andl-master.sln

call "C:\Program Files (x86)\Microsoft Visual Studio 14.0\common7\tools\vsvars32.bat"
msbuild Andl-master.sln /t:Clean;Build /p:Configuration=Release