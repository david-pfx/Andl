: Quick run through of available scripts

call ..\setvars.bat

@if exist out.txt rm out.txt
@if exist andl.sandl rd andl.sandl /s

%binpath%\Andl %1 TestSetup.andl >> out.txt
for %%f in (suite*.andl) (
  echo %f
  echo %f >> out.txt
  %binpath%\Andl %1 %f >> out.txt
)
:@grep -vx ".*True" out.txt
type out.txt

