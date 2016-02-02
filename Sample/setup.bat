echo Setup for samples

: remove all existing databases
for /d %%f in (*.sandl) do rd %%f /s

: run setup programs
..\bin\andl /1 setup.andl
..\bin\andl /1 setup-workbench.andl workbench 
..\bin\andl /1 setup-chinook.andl chinook_sqlite.sqandl

:done
