: build Andl release

git clone Andl Andl-rls -b master
cd Andl-rls

c:\sw\nuget restore Andl-master.sln

call "C:\Program Files (x86)\Microsoft Visual Studio 14.0\common7\tools\vsvars32.bat"
msbuild andl-master /t:Clean;Release