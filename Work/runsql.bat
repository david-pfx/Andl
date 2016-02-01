: Run prog and capture SQL

  ..\andl.Compiler\bin\debug\andl.compiler /x %1 |! gawk "/Execute: (.*)/ { $1=\"\";print $0 }" >sql.txt

c:\sw\sqlite\sqlite3 -init sql.txt

