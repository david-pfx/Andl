echo Setup for sample apps

call ..\setvars.bat

for /d %%f in (*.sandl) do rd %f /s
%binpath%\andl webhostspsetup.andl
