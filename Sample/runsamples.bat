: Quick run through of available samples
: usage: all, sql, pg

@setlocal
call ..\setvars.bat
set andl=%binpath%\Andl /1 %*
if (%1)==(all) set andl=%binpath%\Andl /1 %2$
if (%1)==(sql) set andl=%binpath%\Andl /1 /s %2$
if (%1)==(sql) set ord=no
if (%1)==(pg) set andl=%binpath%\AndlPg /1 /p /d %2$
if (%1)==(pg) set ord=no

@if exist out.txt rm out.txt
@rm *.sqandl
for /d %%f in (*.sandl) do rd %%f /s /q
copy chinook.sqlite chinook_sqlite.sqandl

@date /t >out.txt

: create catalog
%andl% >> out.txt setup.andl 
%andl% >> out.txt sample1.andl
%andl% >> out.txt sample2.andl
%andl% >> out.txt sample3.andl
%andl% >> out.txt sample4.andl
if not (%ord%)==(no) %andl% >> out.txt sample5.andl
%andl% >> out.txt SPPsample1.andl
%andl% >> out.txt DbixCdSample.andl
%andl% >> out.txt family_tree.andl
%andl% >> out.txt 100doors.andl
%andl% >> out.txt 99bottles.andl
%andl% >> out.txt fibonacci.andl
if not (%ord%)==(no) %andl% >> out.txt recursive.andl
if not (%ord%)==(no) %andl% >> out.txt sudoku.andl
: very slow
if (%1)==(all) %andl% >> out.txt mandelbrot.andl
: these for workbench
%andl% >> out.txt setup-workbench.andl
if (%1)==(sql) %andl% >> out.txt setup-chinook.andl chinook_sqlite.sqandl
if (%1)==(sql) %andl% >> out.txt chinook.andl chinook_sqlite.sqandl

@grep -ni "exception" out.txt
@grep -ni "*** Compiled" out.txt
@grep -ni "Fatal:" out.txt
