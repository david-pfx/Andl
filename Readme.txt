Andl is A New Database Language. See andl.org.

Andl does what SQL does, but it is not SQL. Andl has been developed as a fully 
featured database programming language following the principles set out by Date 
and Darwen in The Third Manifesto. It includes a full implementation of the 
Relational Model published by E.F. Codd in 1970, an advanced extensible type 
system, database updates and other SQL-like capabilities in a novel and highly 
expressive syntax.

FIRST DO THIS
=============

Grab the binary release and unzip it somewhere.

Go to the Sample folder in a command prompt and run the following commands.
    C>setup.bat         -- set up the sample databases
    C>setupsql.bat      -- set up to use sqlite, if you have it (see below)
    C>workbench.bat     -- run the workbench (next section)

If you like to use the command line, then try these
    C>run /?            -- view the command line arguments
    C>run               -- run test.andl, a tiny script
    C>run sample1.andl
    C>run sample2.andl
    C>run sample3.andl
    C>run sample4.andl
    C>run sample5.andl


WORKBENCH
=========

The Workbench is an interactive program to view a database amd its catalog, and 
to execute queries.  

1. Choose the 'sample' database and see the relations and contents of its catalog.
2. The Andl program 'workbench.andl' is loaded by default. Press F5 to run it.
3. Press F7 to reload the catalog and F5 to run it again.
4. Or try Ctrt+N for a new program and Ctrl+F7 for a new catalog.

Function keys
-------------
    F5 to run the current program in its entirety.
    Select text and F5 to run part of a program as a query.
    F7 to reload the catalog.
    Ctrl+F7 to load a new empty catalog.

    Here are the sample programs.
        sample1.andl            -- scalar expressions
        sample2.andl            -- basic relational expressions
        sample3.andl            -- advanced relational expresions
        sample4.andl            -- more complex examples
        sample5.andl            -- ordering and grouping (like SQL Window)

    Also take a look at:
        DbixCdSample.andl       -- converted SQL sample
        family_tree.andl        -- recursive self-join (like)
        SPPsample1.andl         -- more converted SQL
        recursive.andl          -- org chart using while (like SQL CTE recursive)
        mandelbrot.andl         -- mandelbrot set
        sudoku-orig.andl        -- sudoku solver
        chinook.andl            -- sqlite database

GETTING SQLITE
==============

This version of Andl can use Sqlite. If you don't already have it somewhere 
accessible, then download it from here: 
https://www.sqlite.org/2015/sqlite-dll-win32-x86-3081002.zip
Just put the DLL somewhere accessible, like on your path or in the Andl\bin folder.
That's all you need.

MORE SQLITE
===========

You can run the same scripts again like this to trigger Sql mode, based on Sqlite. 
Don't try sample5, it won't work in Sql mode.

    C>run setup.andl /s
    C>run sample1.andl /s
    C>run sample2.andl /s
    C>run sample3.andl /s
    C>run sample4.andl /s

This time all relations are stored in Sqlite. Apart from performance and one 
unimplemented feature, the behaviour is identical. If you like, you can use 
the sqlite3.exe program to verify that this is so.

    C>sqlite3.exe sample_sqlite.sqandl ".dump"

Now try this, which requires the Chinook database for Sqlite. You can find it 
here, prebuilt. https://chinookdatabase.codeplex.com/#.

    C>ren Chinook.sqlite Chinook_Sqlite.sqandl
    C>run chinook.andl Chinook_Sqlite.sqandl /s

This uses the widely distributed Chinkook database in its native form. Obviously 
it only works with /s.

BUILDING ANDL

The project should build 'out of the box' in Visual Studio 2013 with the .NET 
Framework 4.5, and possibly earlier versions. It builds an executable program 
that compiles and runs scripts. The default script is test.andl.

It also stores compiled programs, types and variables in a catalog, where they 
can be used by subsequent programs.

LICENCE

This initial version of Andl is free for any kind of experimental use, especially
helping to make it better. For now, the licence does not grant rights for 
distribution or commercial use. That will have to wait until I can choose the 
right licence, which depends a lot on who might want to use it.

Please contact me with any questions or suggestions at david@andl.org.

