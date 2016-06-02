echo Run Host sample
: usage: run-host [sql] [/n]

@setlocal
call ..\setvars.bat
set andl=%binpath%\Andl
if (%1)==(sql) set andl=%binpath%\Andl /s
set host=%binpath%\Andl.Host /1 %*
if (%1)==(sql) set host=%binpath%\Andl.Host /1 /s %2 %3 %4 %5

@if exist out.txt rm out.txt
@rm *.sqandl
for /d %%f in (*.sandl) do rd %%f /s /q

%andl% hostsetup.andl
%host%
