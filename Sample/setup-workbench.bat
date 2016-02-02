echo Setup for workbench

for /d %%f in (*.sandl) do rd %%f /s
..\debug\andl setup-workbench.andl workbench
