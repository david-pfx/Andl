: zip-rls.bat -- zip release files into upload

@if "%1"="" echo"Usage: zip-rls <dirpath>
@if "%1"="" exit /b

setlocal
set rlsver=v10b2
for /f %%i in ('powershell get-date -format "{yyMMdd}"') do set rlsdate=%%i 
set zname=andl-%rlsver%-%rlsdate%.zip

if exist %zname% del %zname% 
zip %zname% %1\readme.txt /r %1\bin /r %1\sample /r %1\test /r %1\thrift
