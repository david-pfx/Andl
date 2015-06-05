Andl is A New Data Language. See andl.org.

Copyright © David M. Bennett 2015 as an unpublished work. All rights reserved.

If you have received this source code directly from me then you are hereby granted 
permission to use it for personal study. For any other use you must ask my 
permission. Not to be copied, distributed or used commercially without my 
explicit written permission.

FIRST DO THIS

Go to the Test folder in a command prompt and run the following commands.
    C>run /?
    C>run 
    C>run setup.andl
    C>run sample1.andl
    C>run sample2.andl
    C>run sample3.andl
    C>run sample4.andl
    C>run sample5.andl

NOW DO THIS

This version of Andl can use Sqlite. If you don't already have it somewhere accessible, then download it from here:
https://www.sqlite.org/2015/sqlite-dll-win32-x86-3081002.zip
Just put the DLL somewhere accessible, like on your path or next to Andl.exe. That's all you need.

Now run the same scripts again like this to trigger Sql mode, based on Sqlite. Don't try sample5, it won't work in Sql mode.

    C>run setup.andl /s
    C>run sample1.andl /s
    C>run sample2.andl /s
    C>run sample3.andl /s
    C>run sample4.andl /s

This time all relations are stored in Sqlite. Apart from performance and one unimplemented feature, the behaviour is identical. 
If you like, you can use the sqlite3.exe program to verify that this is so.

    C>sqlite3.exe andltest.sqlite ".dump"

Now try this, which requires the Chinook database for Sqlite. You can find it here, prebuilt. https://chinookdatabase.codeplex.com/#.

    C>run chinook.andl Chinook_Sqlite.sqlite /s

This uses the widely distributed Chinkook database in its native form. Obviously it only works with /s.

Also take a look at:
    C>run DbixCdSample.andl
    C>run family_tree.andl
    C>run SPPsample1.andl

BUILDING ANDL

The project should build 'out of the box' in Visual Studio 2013 with the .NET 
Framework 4.5, and possibly earlier versions. It builds an executable program that 
compiles and runs scripts. The default script is test.andl.

It also stores compiled programs, types and variables in a catalog, where they can be used
by subsequent programs.

Please contact me with any questions or suggestions at david@andl.org.

