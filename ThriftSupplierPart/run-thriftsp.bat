@echo Run Thrift Supplier Part sample
: usage: sql

@setlocal
@call ..\setvars.bat
set andl=%binpath%\Andl /1 %*
if (%1)==(sql) set andl=%binpath%\Andl /1 /s %2$
set thrift=%binpath%\Andl.Thrift /1 /o %2$
if (%1)==(sql) set thrift=%binpath%\Andl.Thrift /1 /s /o %2$

@rm *.sqandl
@for /d %%f in (*.sandl) do rd %%f /s /q
@del *.thrift
@for /d %f in (gen-*) do rd %f /s /q

%andl% ThriftSupplierPart.andl sp -t %*
%thriftexe% --gen csharp ThriftSupplierPart.thrift

start %thrift% sp %*
%binpath%\ThriftSupplierPart.exe %*
