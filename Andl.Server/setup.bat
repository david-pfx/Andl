echo Setup for sample apps

for /d %f in (*.sandl) do rd %f /s
..\debug\andl websprestsetup.andl sprest
..\debug\andl webspapisetup.andl spapi
..\debug\andl webemprestsetup.andl emprest
