echo Setup for samples

: remove all existing databases
for /d %%f in (*.sandl) do rd %%f /s

: run setup programs
..\bin\andl setup.andl sample /cnu
..\bin\andl setup-workbench.andl workbench /cnu
