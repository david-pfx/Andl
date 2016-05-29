using System;
using System.Collections.Generic;
using Npgsql;
using static Andl.PgClient.PostgresLibPqInterop;
using System.Text.RegularExpressions;
using Andl.Common;
using System.IO;

namespace Andl.PgClient {
  /// <summary>
  /// Mainline for Andl Postgres compiler
  /// TODO: combine them?
  /// </summary>
  class Program {
    const string AndlVersion = "AndlPg 1.0";
    const string Help = "AndlPg <script.ext> [<database name>] [/options]\n"
      + "\t\tScript extension must be andl, sql, pgsql or pgs.\n"
      + "\t\tDefault script is test.andl, database is 'db'.\n"
      + "\t/n\tn=1 to 4, set tracing level";
    static readonly Dictionary<string, Action<string>> _options = new Dictionary<string, Action<string>> {
      { "s", (a) => _settings["Sql"] = "true" },   // for compatibility
      { "p", (a) => { _usepreamble = true; _usepostamble = (a != "o"); } },
    };
    static Dictionary<string, string> _settings = new Dictionary<string, string>();
    static bool _usepreamble = false;
    static bool _usepostamble = false;

    static TextWriter Out { get { return _output; } }
    static TextWriter _output;

    static void Main(string[] args) {
      Logger.Open(1);
      _output = Console.Out;
      _output.WriteLine(AndlVersion);
      var options = OptionParser.Create(_options, Help);
      if (!options.Parse(args))
        return;
      var path = options.GetPath(0) ?? "test.andl";
      var database = options.GetPath(1) ?? "db";

      try {
        if (!File.Exists(path)) throw ProgramError.Fatal($"file does not exist: {path}");
        var input = new StreamReader(path).ReadToEnd();

        var conn = ConnectionInfo.Create("localhost", "postgres", "zzxx", database);
        var pgw = WrapLibpq.Create(conn, _output);
        // use npgsql instead
        //var conn = ConnectionInfo.Create("localhost", "admin", "zzxx", "Try1");
        //var pgw = WrapNpgsql.Create(conn);

        if (_usepreamble) pgw.RunSql(Boilerplate.Preamble, "preamble");

        switch (Path.GetExtension(path)) {
        case ".andl":
          pgw.Compile(input, path);
          break;
        case ".sql":
        case ".pgs":
        case ".pgsql":
          pgw.RunSql(input, path);
          break;
        default:
          throw ProgramError.Fatal($"no action defined for {path}");
        }

        if (_usepostamble) pgw.RunSql(Boilerplate.Postamble, "postamble");

        pgw.Close();
      } catch (ProgramException ex) {
        _output.WriteLine(ex.Message);
        return;
      } catch (Exception ex) {
        _output.WriteLine($"Unexpected exception: {ex.ToString()}");
        return;
      }
    }

  }

  /// <summary>
  /// Create a connection string not tied to ADO
  /// </summary>
  public class ConnectionInfo {
    public string Host { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string Database { get; set; }
    public string AdoConnectionString {
      get { return $"Host={Host};Username={Username};password={Password};Database={Database}"; }
    }
    public string PgConnectionString {
      get { return $"host={Host} user={Username} password={Password} dbname={Database}"; }
    }

    public static ConnectionInfo Create(string host, string username, string password, string database) {
      return new ConnectionInfo {
        Host = host,
        Username = username,
        Password = password,
        Database = database,
      };
    }
  }

  /// <summary>
  /// Error handling
  /// </summary>
  public class ProgramException : Exception {
    internal ProgramException(string message) : base(message) { }
  }

  public class ProgramError {
    public static ProgramException Fatal(string message) {
      return new ProgramException("Fatal: " + message);
    }
  }

  /// <summary>
  /// Preprocess a script
  /// Lines separated by newline; commands end with ';'
  /// </summary>
  public class ScriptLines {
    const string eof = "<eof>";
    public string[] Lines { get { return _lines.ToArray(); } }
    List<string> _lines = new List<string>();

    public static ScriptLines Create(TextReader reader) {
      var sl = new ScriptLines();
      var fline = "";
      for (;;) {
        var line = (reader.ReadLine() ?? eof).Trim();
        if (line == eof) break;
        if (line.StartsWith("--")) continue;
        fline += " " + line;
        if (fline.EndsWith(";")) {
          sl._lines.Add(fline);
          fline = "";
        }
      }
      return sl;
    }
  } 

  /// <summary>
  /// Manage boilerplate code
  /// </summary>
  public class Boilerplate {
    // preamble for loading plandl. note lines end in ';'
    static string plandl_path = @"D:/MyDocs/dev/vs14/Andl/x64/Debug/plandl";
    static string gateway_path = @"D:\MyDocs\dev\vs14\Andl\Debug\Andl.Gateway.dll";
    static int _noisy { get { return Logger.Level; } }

    public static string Preamble {
      get { return $@"
DROP FUNCTION IF EXISTS plandl_call_handler() CASCADE;
CREATE OR REPLACE FUNCTION plandl_call_handler() RETURNS language_handler AS '{plandl_path}' LANGUAGE C;
    CREATE OR REPLACE LANGUAGE plandl HANDLER plandl_call_handler;
CREATE OR REPLACE FUNCTION plandl_compile(program text, source text) returns text
    AS '{gateway_path}|Noisy={_noisy}' LANGUAGE plandl;"; }    // '|Debug,Noisy=2' 
    }

    public static string Postamble {
      get { return @"DROP FUNCTION plandl_call_handler() CASCADE;"; }
    }
  }

  /// <summary>
  /// Interface for access by different methods
  /// 
  /// throws ProgramError
  /// </summary>
  interface IWrapPostgres {
    void Close();
    void Compile(string program, string source);
    void RunSql(string program, string source);
  }

  ///==========================================================================
  /// <summary>
  /// Access Postgres via libpq
  /// </summary>
  class WrapLibpq : IWrapPostgres {
    IntPtr _connection;
    TextWriter _output;

    static internal WrapLibpq Create(ConnectionInfo conn, TextWriter output) {
      Logger.WriteLine(1, $"Connect {conn.PgConnectionString}");
      var pgw = new WrapLibpq() { _output = output };
      pgw.Connect(conn);
      return pgw;
    }

    private void Connect(ConnectionInfo conn) {
      _connection = PQconnectdb(conn.PgConnectionString);
      if (PQstatus(_connection) != ConnStatusType.CONNECTION_OK)
        throw ProgramError.Fatal($"Connection failed: {PQerrorMessageW(_connection)}.");

      PQnoticeProcessorCallback cb = (a, m) => WriteNotice(m);
      PQsetNoticeProcessor(_connection, cb, IntPtr.Zero);
    }

    void WriteNotice(string msg) {
      Logger.WriteLine(2, $"(cb) {msg.TrimEnd()} (cb)");
    }

    public void Close() {
      PQfinish(_connection);
    }

    public void Compile(string program, string source) {
      Logger.WriteLine(1, $"Compile {source}");
      var compilecmd = "SELECT plandl_compile($1, $2)";
      var switches = $"#noisy {Logger.Level}\n";
      ExecSql("BEGIN", new string[] { });
      ExecSql(compilecmd, new string[] { switches + program, source });
      ExecSql("COMMIT", new string[] { });
    }

    public void RunSql(string program, string source) {
      Logger.WriteLine(1, $"RunSql {source}");
      var proglines = ScriptLines.Create(new StringReader(program));
      foreach (var line in proglines.Lines)
        ExecSql(line, new string[] { });
    }

    void ExecSql(string sql, string[] args) {
      Logger.WriteLine(1, $">>> {sql}");
      var result = PQexecParams(_connection, sql, args.Length, IntPtr.Zero, args, IntPtr.Zero, IntPtr.Zero, 0);
      var status = PQresultStatus(result);
      if (status == ExecStatusType.PGRES_COMMAND_OK) {
        Logger.WriteLine(1, "OK");
      } else if (status == ExecStatusType.PGRES_TUPLES_OK) {
        if (Logger.Level >= 1) ShowSingleTuple(result, _output);
      } else throw ProgramError.Fatal($"(Exec Sql) status:{status} : {PQresultErrorMessageW(result)}.");
      PQclear(result);
    }

    void ShowSingleTuple(IntPtr result, TextWriter output) {
      if (PQnfields(result) >= 1 && PQntuples(result) >= 1) {
        output.WriteLine(PQgetvalueW(result, 0, 0));
      } else output.WriteLine("<No data>");
    }

  }

  ///==========================================================================
  /// <summary>
  /// Access Postgres via Npgsql
  /// </summary>
  class WrapNpgsql : IWrapPostgres {

    NpgsqlConnection _connection;
    TextWriter _output;

    static internal WrapNpgsql Create(ConnectionInfo conn, TextWriter output) {
      Logger.WriteLine(1, $"Connect {conn.AdoConnectionString}");
      var pgw = new WrapNpgsql() { _output = output };
      try {
        pgw.Connect(conn);
      } catch (Exception ex) {
        throw ProgramError.Fatal($"Connection failed: {ex.Message}.");
      }
      return pgw;
    }

    private void Connect(ConnectionInfo conn) {
      _connection = new NpgsqlConnection(conn.AdoConnectionString);
      _connection.Open();
    }

    public void Close() {
      _connection.Close();
    }

    public void Compile(string program, string source) {
      Logger.WriteLine(1, $"Compile {program}");
      using (var cmd = new NpgsqlCommand()) {
        cmd.Connection = _connection;
        cmd.CommandText = "SELECT andl_compile(@program, @source)";
        cmd.Parameters.AddWithValue("@program", program);
        cmd.Parameters.AddWithValue("@source", source);
        try {
          _output.WriteLine(cmd.ExecuteScalar() as string);
        } catch (Exception ex) {
          throw ProgramError.Fatal($"Compile failed: {ex.Message}.");
        }
      }
    }

    public void RunSql(string program, string source) {
      Logger.WriteLine(1, $"RunSql {source}");
      using (var cmd = new NpgsqlCommand()) {
        cmd.Connection = _connection;
        cmd.CommandText = program;
        try {
          _output.WriteLine(cmd.ExecuteScalar() as string);
        } catch (Exception ex) {
          throw ProgramError.Fatal($"Sql failed: {ex.Message}.");
        }
      }
    }

  }
}
