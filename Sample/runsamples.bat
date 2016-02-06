: Quick run through of available samples - dev only

call ..\setvars.bat

@if exist out.txt rm out.txt
@if exist testandl.sqlite rm testandl.sqlite
for /d %%f in (*.sandl) do rd %%f /s

@date /t >out.txt

: create catalog
%binpath%\Andl /1 %1 >> out.txt setup.andl 
%binpath%\Andl /1 %1 >> out.txt sample1.andl
%binpath%\Andl /1 %1 >> out.txt sample2.andl
%binpath%\Andl /1 %1 >> out.txt sample3.andl
%binpath%\Andl /1 %1 >> out.txt sample4.andl
%binpath%\Andl /1 %1 >> out.txt sample5.andl
%binpath%\Andl /1 %1 >> out.txt SPPsample1.andl
%binpath%\Andl /1 %1 >> out.txt DbixCdSample.andl
%binpath%\Andl /1 %1 >> out.txt family_tree.andl
%binpath%\Andl /1 %1 >> out.txt recursive.andl
%binpath%\Andl /1 %1 >> out.txt sudoku-logic.andl
%binpath%\Andl /1 %1 >> out.txt chinook.andl chinook_sqlite.sqandl
: uncomment to run, but very slow
:%binpath%\Andl /1 %1 >> out.txt mandelbrot.andl

@grep -n "False" out.txt
@grep -ni "exception" out.txt
@grep -ni "error" out.txt
