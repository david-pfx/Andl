: build Andl release

@if "%1"="" echo"Usage: build-rls <dirpath>
@if "%1"="" exit /b

@setlocal
@set rlsdir=%1

if not exist %rlsdir% git clone Andl %rlsdir% -b master
@cd %rlsdir%

c:\sw\nuget restore Andl-master.sln

call "C:\Program Files (x86)\Microsoft Visual Studio 14.0\common7\tools\vsvars32.bat"
msbuild Andl-master.sln /t:Clean;Build /p:Configuration=Release

pushd ThriftTest
call setup
popd
pushd ThriftPartSupplier
call setup
popd

msbuild Andl-master.sln /t:Build /p:Configuration=Release

echo"Run zip-rls to create upload.