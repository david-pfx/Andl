echo Setup for sample apps

for /d %%f in (*.sandl) do rd %f /s
..\debug\andl webhostspsetup.andl
