: zip-rls.bat -- zip release files into upload

setlocal
set rlsver=v10b2
set rlsdate=16f204
del andl-%rlsver%-%rlsdate%.zip 
zip andl-%rlsver%-%rlsdate%.zip readme.txt /r bin /r sample /r test /r thrift
