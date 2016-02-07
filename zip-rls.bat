: zip-rls.bat -- zip release files into upload

setlocal
set rlsver=v10b2
for /f %%i in ('powershell get-date -format "{yyMMdd}"') do set rlsdate=%%i 
set zname=andl-%rlsver%-%rlsdate%.zip

if exist %zname% del %zname% 
zip %zname% readme.txt /r bin /r sample /r test /r thrift
