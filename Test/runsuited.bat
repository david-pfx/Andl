: Quick run through of available scripts

@if exist out.txt rm out.txt
@if exist andl.sandl rd andl.sandl /s

..\Debug\Andl %1 TestSetup.andl >> out.txt
for %%f in (suite*.andl) (
  echo %f
  echo %f >> out.txt
  ..\Debug\Andl %1 %f >> out.txt
)
:@grep -vx ".*True" out.txt
type out.txt

