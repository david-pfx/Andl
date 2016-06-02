: Quick run through of available scripts
: usage: all, sql, pg

@echo off
setlocal
call ..\setvars.bat
set andl=%binpath%\Andl /1 %*
if (%1)==(all) set andl=%binpath%\Andl /1 %2 %3 %4 %5
if (%1)==(sql) set andl=%binpath%\Andl /1 /s %2 %3 %4 %5
if (%1)==(sql) set ord=no
if (%1)==(pg) set andl=%binpath%\AndlPg /1 /p /d %2 %3 %4 %5
if (%1)==(pg) set ord=no

if exist out.txt rm out.txt
rm *.sqandl
for /d %%f in (*.sandl) do rd %%f /s /q

date /t >out.txt
%andl% SetupSuite.andl >> out.txt
for %%f in (suite*.andl) do (
  echo %andl% %%f
  %andl% %%f >> out.txt
)

grep -ni "with error" out.txt
grep -ni "terminated" out.txt
grep -ni "tests = [0-9]" out.txt

