: set variables for running programs (relative from project folder)

set binpath=..\release
if exist ..\debug set binpath=..\debug
set andlexe=%binpath%\andl.exe
set thriftexe=..\thrift-0.9.3.exe

: preferred, but not in new official version
:set thriftexe=..\packages\Thrift.0.9.1.3\tools\thrift-0.9.1.exe