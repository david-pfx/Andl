using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Andl.Sqlite;

namespace Andl.Runtime {
  public class Templater {
    public string Template { get; private set; }
    StringBuilder _builder = new StringBuilder();

    static Dictionary<string, string> _templates = new Dictionary<string, string> {
      { "Create",         "DROP TABLE IF EXISTS [<table>] \n" +
                          "CREATE TABLE [<table>] ( <coldefs>, UNIQUE ( <colnames> ) ON CONFLICT IGNORE )" },
      { "SelectAll",      "SELECT <namelist> FROM [<table>]" },
      { "SelectRename",   "SELECT <namelist> FROM <select>" },
      { "SelectAs",       "SELECT DISTINCT <namelist> FROM <select>" },
      { "SelectAsGroup",  "SELECT DISTINCT <namelist> FROM <select> <groupby>" },
      { "SelectJoin",     "SELECT DISTINCT <namelist> FROM <select1> JOIN <select2> <using>" },
      { "SelectAntijoin", "SELECT DISTINCT <namelist> FROM <select1> [_a_] WHERE NOT EXISTS (SELECT 1 FROM <select2> [_b_] WHERE <nameeqlist>)" },
      { "SelectSet",      "<select1> <setop> <select2>" },
      { "SelectSetName",  "SELECT DISTINCT <namelist> FROM <select1> <setop> SELECT <namelist> FROM <select2>" },
      { "SelectCount",    "SELECT COUNT(*) FROM <select>" },
      { "SelectOneWhere", "SELECT 1 <whereexist>" },

      { "InsertNamed",    "INSERT INTO [<table>] ( <namelist> ) <select>" },
      { "InsertSelect",   "INSERT INTO [<table>] <select>" },
      { "InsertValues",   "INSERT INTO [<table>] ( <namelist> ) VALUES ( <valuelist> )" },
      { "InsertJoin",     "INSERT INTO [<table>] ( <namelist> ) <select>" },
      { "Delete",         "DELETE FROM [<table>] WHERE <pred>" },
      { "Update",         "UPDATE [<table>] SET <namesetlist> WHERE <pred>" },

      { "WhereExist",     "WHERE <not> EXISTS ( <select1> <setop> <select2> )" },
      { "WhereExist2",    "WHERE <not> EXISTS ( <select1> <setop> <select2> ) AND <not> EXISTS ( <select2> <setop> <select1> )" },
      { "Where",          "WHERE <expr>" },
      { "Using",          "USING ( <namelist> )" },

      { "OrderBy",        "ORDER BY <ordcols>" },
      { "GroupBy",        "GROUP BY <grpcols>" },
      { "EvalFunc",       "<func>(<lookups>)" },
      { "Coldef",         "[<colname>] <coltype>" },
      { "Name",           "[<name>]" },
      { "NameEq",         "[_a_].[<name>] = [_b_].[<name>]" },
      { "NameAs",         "[<name1>] AS [<name2>]" },
      { "NameSet",        "[<name1>] = [<name2>]" },
      { "Value",          "<value>" },
      { "Param",          "?<param>" },
    };

    public override string ToString() {
      return _builder.ToString();
    }

    public static Templater Create(string template) {
      var t = new Templater { Template = _templates[template] };
      return t;
    }

    public void Append(string text) {
      _builder.Append(text);
    }

    public static string Process(string template, Dictionary<string, SubstituteDelegate> dict, int index = 0) {
      Logger.Assert(_templates.ContainsKey(template), template);
      var t = new Templater { Template = _templates[template] };
      return t.Process(dict, index).ToString();
    }

    public Templater Process(Dictionary<string, SubstituteDelegate> dict, int index = 0) {
      var pos = 0; 
      while (true) {
        var apos = Template.IndexOf('<', pos);
        if (apos == -1) {
          _builder.Append(Template, pos, Template.Length - pos);
          break;
        } else {
          _builder.Append(Template, pos, apos - pos);
          var bpos = Template.IndexOf('>', ++apos);
          var token = Template.Substring(apos, bpos - apos);
          Logger.Assert(dict.ContainsKey(token), Template +":" + token);
         _builder.Append(dict[token](index));
          pos = bpos + 1;
        }
      }
      return this;
    }

    public Templater Process(Dictionary<string, SubstituteDelegate> dict, int howmany, string delim) {
      for (var i = 0; i < howmany; ++i) {
        if (i > 0)
          _builder.Append(delim);
        Process(dict, i);
      }
      return this;
    }
  }

  /// <summary>
  /// Implement Sql generation
  /// 
  /// Uses Templater and SqlTarget
  /// </summary>
  public class SqlGen {
    // Types suitable for use in column definitions
    // see https://www.sqlite.org/datatype3.html S2.2
    static Dictionary<DataType, string> _datatypetosql = new Dictionary<DataType, string> {
      { DataTypes.Binary, "BLOB" },
      { DataTypes.Bool, "INTEGER" },
      { DataTypes.Number, "REAL" },
      { DataTypes.Time, "TEXT" },
      { DataTypes.Text, "TEXT" },
      { DataTypes.Table, "BLOB" },
      { DataTypes.Row, "BLOB" },
      { DataTypes.User, "BLOB" },
    };

    static Dictionary<JoinOps, string> _joinoptosql = new Dictionary<JoinOps, string> {
      { JoinOps.UNION, "UNION" },
      { JoinOps.INTERSECT, "INTERSECT" },
      { JoinOps.MINUS, "EXCEPT" },
    };

    public const string Ascending = "ASC";
    public const string Descending = "DESC";

    // functions to convert value into SQL literal format
    public delegate string ValueToSqlDelegate(TypedValue value);
    static Dictionary<DataType, ValueToSqlDelegate> _valuetosql = new Dictionary<DataType, ValueToSqlDelegate> {
      { DataTypes.Binary, x => BinaryLiteral(((BinaryValue)x).Value) },
      { DataTypes.Bool, x => ((BoolValue)x).Value ? "1" : "0" },
      { DataTypes.Number, x => x.Format() },
      { DataTypes.Row, x => Quote(x.ToString()) },
      { DataTypes.Time, x => Quote(x.Format()) },
      { DataTypes.Text, x => Quote(x.ToString()) },
      { DataTypes.Table, x => Quote(x.ToString()) },
      { DataTypes.User, x => Quote(x.ToString()) },
    };

    int AccBase { get; set; }

    //----- sql generating fragments

    public string Combine(params string[] args) {
      StringBuilder sb = new StringBuilder();
      foreach (var arg in args) {
        if (arg != null) {
          if (sb.Length > 0)
            sb.Append(" ");
          sb.Append(arg);
        }
      }
      return sb.ToString();
    }

    // return the corresponding Sql type 
    public string ColumnType(DataType datatype) {
      Logger.Assert(_datatypetosql.ContainsKey(datatype.BaseType), datatype);
      return _datatypetosql[datatype.BaseType];
    }

    // return the corresponding join op
    public string JoinOp(JoinOps joinop) {
      Logger.Assert(_joinoptosql.ContainsKey(joinop), joinop);
      return _joinoptosql[joinop];
    }

    // return a literal Sql value
    public static string ColumnValue(TypedValue value) {
      return _valuetosql[value.DataType](value);
    }

    // Quote an Sql literal string
    public static string Quote(string value) {
      return "'" + value.Replace("'", "''") + "'";
    }

    public static string BinaryLiteral(byte[] bytes) {
      StringBuilder sb = new StringBuilder();
      foreach (var b in bytes)
        sb.Append(b.ToString("x2"));
      return "x'" + sb + "'";
    }

    public static byte[] ToBinary(ExpressionBlock expr) {
      using (var writer = PersistWriter.Create()) {
        writer.Write(expr);
        return writer.ToArray();
      }
    }

    //public static string BinaryLiteral(ExpressionBlock expr) {
    //  return BinaryLiteral(ToBinary(expr));
    //}

    public static string FuncName(ExpressionBlock expr) {
      if (expr.IsOpen) return "EVAL" + expr.Serial.ToString();
      if (expr.HasFold) return "EVALA" + expr.Serial.ToString();
      return null;
    }

    static int _tempid = 0;

    public string TempName() {
      //return "_temp_T_" + (++_tempid).ToString();
      return "temp.T_" + (++_tempid).ToString();
    }

    // generate a plain delimited name list
    public string NameList(string[] names) {
      if (names.Length == 0) return "_dummy_";
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "name", (x) => names[x] },
      };
      return Templater.Create("Name").Process(coldict, names.Length, ", ").ToString();
    }

    public string NameList(DataHeading heading) {
      return NameList(heading.Columns.Select(x => x.Name).ToArray());
    }

    public string NameEqList(string[] names) {
      if (names.Length == 0) return "1=1";
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "name", (x) => names[x] },
      };
      return Templater.Create("NameEq").Process(coldict, names.Length, " AND ").ToString();
    }

    public string NameEqList(DataHeading heading) {
      return NameEqList(heading.Columns.Select(x => x.Name).ToArray());
    }

    //public string NameSetList(ExpressionBlock[] exprs) {
    //  var coldict = new Dictionary<string, SubstituteDelegate> {
    //    { "name1", (x) => exprs[x].Name },
    //    { "name2", (x) => EvalFunc(exprs[x]) },
    //  };
    //  return Templater.Create("NameSet").Process(coldict, exprs.Length, ", ").ToString();
    //}

    // Hand-crafted AS terms
    public string NameSetList(ExpressionBlock[] exprs) {
      var ss = new List<string>();
      foreach (var expr in exprs) {
        if (expr.IsProject) {}
        else if (expr.IsRename)
          ss.Add(String.Format("[{0}] = [{1}]", expr.Name, expr.OldName)); // CHECK:will this work?
        else
          ss.Add(String.Format("[{0}] = {1}", expr.Name, EvalFunc(expr)));
      }
      return string.Join(", ", ss);
    }

    // Hand-crafted AS terms
    public string NameAsList(ExpressionBlock[] exprs) {
      if (exprs.Length == 0) return "NULL as _dummy_";
      var sb = new StringBuilder();
      foreach (var expr in exprs) {
        if (sb.Length > 0) sb.AppendFormat(", ");
        if (expr.IsProject)
          sb.AppendFormat("[{0}]", expr.Name);
        else if (expr.IsRename)
          sb.AppendFormat("[{0}] AS [{1}]", expr.OldName, expr.Name);
        else
          sb.AppendFormat("{0} AS [{1}]", EvalFunc(expr), expr.Name);
      }
      return sb.ToString();
    }

    public string ColDefs(DataHeading heading) {
      if (heading.Degree == 0) return "[_dummy_] TEXT";
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "colname", (x) => heading.Columns[x].Name },
        { "coltype", (x) => ColumnType(heading.Columns[x].DataType) },
      };
      return Templater.Create("Coldef").Process(coldict, heading.Degree, ", ").ToString();
    }

    public string ColOrders(ExpressionBlock[] exprs) {
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "colname", (x) => exprs[x].Name },
        { "coltype", (x) => exprs[x].IsDesc ? Descending : Ascending },
      };
      return Templater.Create("Coldef").Process(coldict, exprs.Length, ", ").ToString();
    }

    public string ValueList(int howmany, SubstituteDelegate subs) {
      if (howmany == 0) return "NULL";
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "value", (x) => subs(x) },
      };
      return Templater.Create("Value").Process(coldict, howmany, ", ").ToString();
    }

    public string ParamList(DataHeading heading) {
      if (heading.Degree == 0) return "NULL";
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "param", (x) => (x+1).ToString() },
      };
      return Templater.Create("Param").Process(coldict, heading.Degree, ", ").ToString();
    }

    public string EvalFunc(ExpressionBlock expr) {
      var lookups = (expr.NumArgs == 0) ? "" : NameList(expr.Lookup.Columns.Select(c => c.Name).ToArray());
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "func", (x) => FuncName(expr) },
        { "lookups", (x) => lookups },
      };
      return Templater.Process("EvalFunc", dict);
    }

    public string Where(ExpressionBlock expr) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "expr", (x) => EvalFunc(expr) },  // FIX: predicate
      };
      return Templater.Process("Where", dict);
    }

    public string OrderBy(ExpressionBlock[] exprs) {
      if (exprs.Length == 0) return "";
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "ordcols", (x) => ColOrders(exprs) },
      };
      return Templater.Process("OrderBy", dict);
    }

    public string GroupBy(ExpressionBlock[] exprs) {
      if (exprs.Length == 0) return "";
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "grpcols", (x) => NameList(DataHeading.Create(exprs)) },
      };
      return Templater.Process("GroupBy", dict);
    }

    public string SelectAs(string tableorquery, ExpressionBlock[] exprs) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select", (x) => tableorquery },
        { "namelist", (x) => NameAsList(exprs) },
      };
      return Templater.Process("SelectAs", dict);
    }

    public string SelectAsGroup(string tableorquery, ExpressionBlock[] exprs) {
      var groups = exprs.Where(e => !e.HasFold).ToArray();
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select", (x) => tableorquery },
        { "namelist", (x) => NameAsList(exprs) },
        { "groupby", (x) => GroupBy(groups) },
      };
      return Templater.Process("SelectAsGroup", dict);
    }

    // Sql to insert multiple rows of data into named table with binding
    public string InsertValuesParam(string tablename, DataTable other) {
      var namelist = NameList(other.Heading);
      var paramlist = ParamList(other.Heading);
      var dict = new Dictionary<string, SubstituteDelegate> {
          { "table", (x) => tablename },
          { "namelist", (x) => namelist },
          { "valuelist", (x) => paramlist },
        };
      return Templater.Process("InsertValues", dict);
    }

    // Sql to insert rows of data into named table as literals
    public string InsertValues(string tablename, DataTable other) {
      var namelist = NameList(other.Heading);
      var tmpl = Templater.Create("InsertValues");
      var rowno = 0;
      foreach (var row in other.GetRows()) { 
        var values = row.Values;
        var valuelist = ValueList(other.Heading.Degree, x => ColumnValue(values[x]));
        var dict = new Dictionary<string, SubstituteDelegate> {
          { "table", (x) => tablename },
          { "namelist", (x) => namelist },
          { "valuelist", (x) => valuelist },
        };
        if (rowno > 0) tmpl.Append("\n");
        tmpl.Process(dict);
      }
      return tmpl.ToString();
    }

    // Sql to delete according to predicate
    public string Delete(string tablename, ExpressionBlock pred) {
      var dict = new Dictionary<string, SubstituteDelegate> {
          { "table", (x) => tablename },
          { "pred", (x) => EvalFunc(pred) },
        };
      return Templater.Process("Delete", dict);
    }

    // Sql to udpate according to predicate and expressions
    public string Update(string tablename, ExpressionBlock pred, ExpressionBlock[] exprs) {
      var dict = new Dictionary<string, SubstituteDelegate> {
          { "table", (x) => tablename },
          { "pred", (x) => EvalFunc(pred) },
          { "namesetlist", (x) => NameSetList(exprs) },
        };
      return Templater.Process("Update", dict);
    }

    // Generate Sql to create this table using name and columns provided
    public string CreateTable(string name, DataTable other) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "table", (x) => name },
        { "coldefs", (x) => ColDefs(other.Heading) },
        { "colnames", (x) => NameList(other.Heading) },
      };
      return Templater.Process("Create", dict);
    }

    public string SelectAll(string tablename, DataTableSql other) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "table", (x) => other.TableName },
        { "namelist", (x) => NameList(other.Heading) },
      };
      return Templater.Process("SelectAll", dict);
    }

    // Actually insert query results into this table
    public string InsertSelect(string tablename, string query) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "table", (x) => tablename },
        { "select", (x) => query },
      };
      return Templater.Process("InsertSelect", dict);
    }

    // Actually insert query results into this table
    public string InsertNamed(string tablename, DataHeading heading, string query) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "table", (x) => tablename },
        { "namelist", (x) => NameList(heading) },
        { "select", (x) => query },
      };
      return Templater.Process("InsertNamed", dict);
    }

    // Generate Sql to insert query results into this table
    public string InsertJoin(string name, DataHeading heading, string query, JoinOps joinop) {
      // ignore joinop for now
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "namelist", (x) => NameList(heading) },
        { "table", (x) => name },
        { "select", (x) => query },
      };
      return Templater.Process("InsertJoin", dict);
    }

    // Using namelist, but nothing for empty heading
    public string Using(DataHeading heading) {
      if (heading.Degree == 0) return "";
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "namelist", (x) => NameList(heading) },
      };
      return Templater.Process("Using", dict);
    }

    // Note: using <nameeqlist> triggers ambiguous column name
    public string SelectJoin(string leftsql, string rightsql, DataHeading newheading, DataHeading joinheading) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select1", (x) => leftsql },
        { "select2", (x) => rightsql },
        { "namelist", (x) => NameList(newheading) },
        { "using", (x) => Using(joinheading) },
      };
      return Templater.Process("SelectJoin", dict);
    }

    public string SelectAntijoin(string leftsql, string rightsql, DataHeading newheading, DataHeading joinheading) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select1", (x) => leftsql },
        { "select2", (x) => rightsql },
        { "namelist", (x) => NameList(newheading) },
        { "nameeqlist", (x) => NameEqList(joinheading) },
      };
      return Templater.Process("SelectAntijoin", dict);
    }

    public string SelectSet(string leftsql, string rightsql, DataHeading newheading, JoinOps joinop) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select1", (x) => leftsql },
        { "select2", (x) => rightsql },
        { "namelist", (x) => NameList(newheading) },
        { "setop", (x) => JoinOp(joinop) },
      };
      return Templater.Process("SelectSetName", dict);
    }

    public string SelectOneWhere(string leftsql, string rightsql, JoinOps joinop, bool equal) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "whereexist", (x) => WhereExist(leftsql, rightsql, joinop, true, equal) },
      };
      return Templater.Process("SelectOneWhere", dict);
    }

    public string WhereExist(string leftsql, string rightsql, JoinOps joinop, bool not, bool twice) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "not", (x) => not ? "NOT" : "" },
        { "select1", (x) => leftsql },
        { "select2", (x) => rightsql },
        { "setop", (x) => JoinOp(joinop) },
      };
      return Templater.Process(twice ? "WhereExist2" : "WhereExist", dict);
    }

    public string SelectCount(string sql) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select", (x) => sql },
      };
      return Templater.Process("SelectCount", dict);
    }

  }
}
