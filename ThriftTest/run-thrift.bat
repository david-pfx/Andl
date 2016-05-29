@echo Run Thrift Test sample
: usage: sql, pg

@setlocal
call ..\setvars.bat
set andl=%binpath%\Andl /1 %*
if (%1)==(sql) set andl=%binpath%\Andl /1 /s %2$
set thrift=%binpath%\Andl.Thrift /1 /o %2$
if (%1)==(sql) set thrift=%binpath%\Andl.Thrift /1 /s /o %2$
if (%1)==(pg) set andl=%binpath%\AndlPg /1 /p %2$

@if exist out.txt rm out.txt
@rm *.sqandl
for /d %%f in (*.sandl) do rd %%f /s /q

%andl% ThriftTest.andl test -t %*
%thriftexe% --gen csharp ThriftTest.thrift

start %thrift% test %*
%binpath%\ThriftTest.exe %*
