: Quick run through of available samples

@if exist out.txt rm out.txt
@if exist testandl.sqlite rm testandl.sqlite

@date /t >out.txt

: create catalog
..\Debug\Andl /1 %1 >> out.txt setup.andl 
..\Debug\Andl /1 %1 >> out.txt sample1.andl
..\Debug\Andl /1 %1 >> out.txt sample2.andl
..\Debug\Andl /1 %1 >> out.txt sample3.andl
..\Debug\Andl /1 %1 >> out.txt sample4.andl
..\Debug\Andl /1 %1 >> out.txt sample5.andl
..\Debug\Andl /1 %1 >> out.txt SPPsample1.andl
..\Debug\Andl /1 %1 >> out.txt DbixCdSample.andl
..\Debug\Andl /1 %1 >> out.txt family_tree.andl
..\Debug\Andl /1 %1 >> out.txt recursive.andl
..\Debug\Andl /1 %1 >> out.txt sudoku-logic.andl
..\Debug\Andl /1 %1 >> out.txt chinook.andl chinook_sqlite.sqandl
: uncomment to run, but very slow
:..\Debug\Andl /1 %1 >> out.txt mandelbrot.andl

@grep -n "False" out.txt
@grep -ni "exception" out.txt
@grep -ni "error" out.txt
