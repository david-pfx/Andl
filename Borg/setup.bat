echo Setting up Borg database and catalog

for /d %%f in (*.sandl) do rd %f /s
..\debug\andl borg.andl borg /t

..\bin\thrift-0.9.2.exe --gen java borg.thrift
