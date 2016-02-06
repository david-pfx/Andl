echo Setting up Thrift database for Test

call ..\setvars.bat

for /d %f in (*.sandl) do rd %f /s
for /d %f in (gen-*) do rd %f /s
del *.thrift

%binpath%\andl ThriftTest.andl test -t

..\bin\thrift-0.9.2.exe --gen csharp ThriftTest.thrift
