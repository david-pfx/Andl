echo Setup for sample apps

call ..\setvars.bat

for /d %f in (*.sandl) do rd %f /s
%binpath%\andl websprestsetup.andl sprest
%binpath%\andl webspapisetup.andl spapi
%binpath%\andl webemprestsetup.andl emprest
