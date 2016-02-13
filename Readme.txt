Andl is A New Database Language. See http://andl.org.

Andl is a New Database Language designed to replace SQL and then go beyond.

Andl is a full programming language with an advanced type system; it is 
relationally complete and has higher order queries; and it has platform 
independent interfacing. So Andl does two things well: perform advanced 
relational queries and build an application data model.

First, Andl can perform relational queries at or beyond the capabilities 
of any SQL dialect. It can do all the ordinary things like select, where 
and join but it can also do generative queries, self-joins, complex 
aggregations, subtotals and running totals (a bit like SQL recursive 
common table expressions and windowing). 

And Andl can provide a complete application backend for any kind of user 
interface on any platform. It can easily be used to program a data model 
as a set of tables just like SQL, but including all the access routines, 
updating logic and interfaces. These can be accessed on any platform: 
mobile, desktop, web or cloud. The user interface can be written in 
any popular language or technology, such as Java, Dot Net, JavaScript 
and using any available communications method.

Sample programs are included to demonstrate these capabilities.

FIRST DO THIS
=============

Grab the binary release and unzip it somewhere.

Go to the Sample folder in a command prompt and run the following commands.
    C>setup.bat         -- set up the sample databases
    C>runwb.bat     -- run the workbench (next section)

If you like to use the command line, then try these:
    C>run /?            -- view the command line arguments
    C>run               -- run test.andl, a tiny script
    C>run sample1.andl
    C>run sample2.andl
    C>run sample3.andl
    C>run sample4.andl
    C>run sample5.andl
    C>run sample6.andl

The default program is 'test.andl' and the default catalog is 'data'.

WORKBENCH
=========

The Workbench is an interactive program to view a database amd its catalog, and 
to execute queries.  

Andl reads program source code, compiles and executes the program and then stores 
compiled operators, types and global variables in a catalog, where they can be used by 
other programs. In the Workbench you can see the contents of the catalog.

1. Choose a database and see the relations and contents of its catalog. Choose Workbench.
2. The Andl program 'workbench.andl' is loaded by default. Press F5 to run it.
3. Press F7 to reload the catalog and F5 to run it again.
4. Or try Ctrt+N for a new program and Ctrl+F7 for a new catalog.

Function keys
-------------
    F5 to run the current program in its entirety.
    Select text and F5 to run part of a program as a query.
    F7 to reload the catalog (you get errors if you try to define something twice).
    Ctrl+F7 to load a new empty catalog.

Here are the sample programs.
    sample1.andl            -- scalar expressions
    sample2.andl            -- basic relational expressions
    sample3.andl            -- advanced relational expresions
    sample4.andl            -- more complex examples
    sample5.andl            -- ordering and grouping (like SQL Window)
    sample6.andl            -- subtyping (work in progress)

Also take a look at:
    DbixCdSample.andl       -- converted SQL sample
    family_tree.andl        -- self-join using functions
    SPPsample1.andl         -- more converted SQL
    recursive.andl          -- self-joins using while (like SQL CTE recursive)
    100doors.andl           -- the 100 doors puzzle
    99bottles.andl          -- the 99 bottles lyrics
    fibonacci.andl          -- 100 fibonacci numbers - fast!
    mandelbrot.andl         -- mandelbrot set using while
    sudoku-orig.andl        -- sudoku solver using while
    chinook.andl            -- sqlite database

SQLITE
======

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

THRIFT
======

Thrift is a platform-agnostic interface and remote procedure call technology.
More details at http://thrift.apache.org/.

Andl has a built-in capability to generate Thrift interfaces, and the Andl.Thrift
project is a Thrift server.

The Thrift folder contains samples for the Thrift implementation. 

BUILDING ANDL
=============

The source code can be downloaded from https://github.com/davidandl/Andl.

The project should build 'out of the box' in Visual Studio 2015 with the .NET 
Framework 4.5, and possibly earlier versions. It builds an executable program 
that compiles and runs Andl programs, and several other components. 

These additional samples require the source release.

HOST
====

Andl.Host is a server for a native Web API and a REPL interface based on WCF. When 
run it is self-testing. It also contains a mini-website that is a REPL client,
which should be launced in a local browser.

SERVER
======

Andl.Server is a server for a JSON and REST Web API based on ASP.NET MVC. When 
run it launches a browser to show sample programs. 

For JSON and REST Web API the backend is Andl and the client is a JavaScript
Single Page Application. The amount of code to produce a working application 
is small.
    
The REPL page is not yet working.

LICENCE
=======

This version of Andl is free for any kind of experimental use, especially
helping to make it better. For now, the licence does not grant rights for 
distribution or commercial use. That will have to wait until I can choose the 
right licence, which depends a lot on who might want to use it.

Andl is built using a variety of components including Sqlite, Newtonsoft Json, 
Pegasus, Avalon Edit, Thrift, Jquery, Knockout. The reader is referred to those 
products for relevant licence terms.

Please contact me with any questions or suggestions at david@andl.org.

