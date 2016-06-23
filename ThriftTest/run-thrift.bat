@echo Run Thrift Test sample
: usage: sql

@echo off
setlocal
call ..\setvars.bat
set andl=%binpath%\Andl /1 /t %*
set thrift=%binpath%\Andl.Thrift /1 /o 
if (%1)==(sql) set andl=%binpath%\Andl /1 /t /s %2 %3 %4 %5
if (%1)==(sql) set thrift=%binpath%\Andl.Thrift /1 /s /o 

if exist out.txt rm out.txt
del *.sqandl /q
for /d %%f in (*.sandl) do rd %%f /s /q
del *.thrift /q
for /d %%f in (gen-*) do rd %%f /s /q

%andl% ThriftTest.andl test >>out.txt
%thriftexe% --gen csharp ThriftTest.thrift

start %thrift% test 
%binpath%\ThriftTest.exe >>out.txt

echo.
echo Results:
grep "complete" out.txt
grep "exception" out.txt


