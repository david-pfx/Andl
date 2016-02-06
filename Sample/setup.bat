echo Setup for samples

call ..\setvars.bat

: remove all existing databases
for /d %%f in (*.sandl) do rd %%f /s

: run setup programs
%binpath%\andl /1 setup.andl
%binpath%\andl /1 setup-workbench.andl workbench 
%binpath%\andl /1 setup-chinook.andl chinook_sqlite.sqandl

:done
