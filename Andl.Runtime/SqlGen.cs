using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Andl.Sql;

namespace Andl.Runtime {
  /// <summary>
  /// Implement Sql generation
  /// 
  /// Uses specified Templater
  /// </summary>
  public class SqlGen {
    // functions to convert value into SQL literal format
    static Dictionary<DataType, Func<TypedValue, string>> _valuetosql = new Dictionary<DataType, Func<TypedValue, string>> {
      { DataTypes.Binary, x => BinaryLiteral(((BinaryValue)x).Value) },
      { DataTypes.Bool, x => ((BoolValue)x).Value ? "1" : "0" },
      { DataTypes.Number, x => x.Format() },
      { DataTypes.Row, x => Quote(x.ToString()) },
      { DataTypes.Time, x => Quote(x.Format()) },
      { DataTypes.Text, x => Quote(x.ToString()) },
      { DataTypes.Table, x => Quote(x.ToString()) },
      { DataTypes.User, x => Quote(x.ToString()) },
    };

    // Which templater determines syntax of SQL generates
    public SqlTemplater Templater { get { return _templater; } set { _templater = value; } }
    SqlTemplater _templater;
    static int _tempid = 0;
    static int _aliasid = 0;

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

    // return a literal Sql value
    static string ColumnValue(TypedValue value) {
      return _valuetosql[value.DataType](value);
    }

    // Quote an Sql literal string
    static string Quote(string value) {
      return "'" + value.Replace("'", "''") + "'";
    }

    static string BinaryLiteral(byte[] bytes) {
      StringBuilder sb = new StringBuilder();
      foreach (var b in bytes)
        sb.Append(b.ToString("x2"));
      return "x'" + sb + "'";
    }

    static byte[] ToBinary(ExpressionBlock expr) {
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

    public string TempName() {
      //return "_temp_T_" + (++_tempid).ToString();
      return "temp.T_" + (++_tempid).ToString();
    }

    string AliasName() {
      return "A_" + (++_aliasid).ToString();
    }

    // generate a plain delimited name list
    string NameList(string[] names) {
      if (names.Length == 0) return "_dummy_";
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "name1", (x) => names[x] },
      };
      return _templater.Create("Name").Process(coldict, names.Length, ", ").ToString();
    }

    string NameList(DataHeading heading) {
      return NameList(heading.Columns.Select(x => x.Name).ToArray());
    }

    string NameEqList(string[] names) {
      if (names.Length == 0) return "1=1";
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "name1", (x) => names[x] },
      };
      return _templater.Create("NameEq").Process(coldict, names.Length, " AND ").ToString();
    }

    string NameEqList(DataHeading heading) {
      return NameEqList(heading.Columns.Select(x => x.Name).ToArray());
    }

    // List of name assignments
    string NameSetList(ExpressionBlock[] exprs) {
      return NameList(exprs, "NameSet");
    }

    // List of name AS: exclude project
    string NameAsList(ExpressionBlock[] exprs) {
      if (exprs.Length == 0) return "NULL as _dummy_";
      return NameList(exprs, "NameAs");
    }

    // List of names with variable subtemplate
    string NameList(ExpressionBlock[] exprs, string template) {
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "~", (x) => exprs[x].IsProject ? "0" : exprs[x].IsRename ? "1" : "2" }, // pick a subtemplate
        { "name1", (x) => exprs[x].Name },
        { "name2", (x) => exprs[x].OldName },
        { "expr", (x) => EvalFunc(exprs[x]) },
      };
      return _templater.Create(template).Process(coldict, exprs.Length, ", ").ToString();
    }

    string NameEqValueList(string[] names) {
      if (names.Length == 0) return "1=1";
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "name1", (x) => names[x] },
        { "value", (x) => Param(x) },
      };
      return _templater.Create("NameEqValue").Process(coldict, names.Length, " AND ").ToString();
    }

    string ColDefs(DataColumn[] columns) {
      //if (heading.Degree == 0) return "_dummy_ TEXT";
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "colname", (x) => columns[x].Name },
        { "coltype", (x) => _templater.ColumnType(columns[x].DataType) },
      };
      return _templater.Create("Coldef").Process(coldict, columns.Length, ", ").ToString();
    }

    string ColOrders(ExpressionBlock[] exprs) {
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "colname", (x) => exprs[x].Name },
        { "coltype", (x) => _templater.SortOrder(exprs[x].IsDesc) },
        //{ "coltype", (x) => exprs[x].IsDesc ? _templater.Descending : _templater.Ascending },
      };
      return _templater.Create("Coldef").Process(coldict, exprs.Length, ", ").ToString();
    }

    string ValueList(int howmany, SubstituteDelegate subs) {
      if (howmany == 0) return "NULL";
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "value", (x) => subs(x) },
      };
      return _templater.Create("Value").Process(coldict, howmany, ", ").ToString();
    }

    string Param(int paramno) {
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "param", (x) => (paramno+1).ToString() },
      };
      return _templater.Create("Param").Process(coldict).ToString();
    }

    string ParamList(DataHeading heading) {
      if (heading.Degree == 0) return "NULL";
      var coldict = new Dictionary<string, SubstituteDelegate> {
        { "param", (x) => (x+1).ToString() },
      };
      return _templater.Create("Param").Process(coldict, heading.Degree, ", ").ToString();
    }

    string EvalFunc(ExpressionBlock expr) {
      var lookups = (expr.NumArgs == 0) ? "" : NameList(expr.Lookup.Columns.Select(c => c.Name).ToArray());
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "func", (x) => FuncName(expr) },
        { "lookups", (x) => lookups },
      };
      return _templater.Process("EvalFunc", dict);
    }

    public string Where(ExpressionBlock expr) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "expr", (x) => EvalFunc(expr) },  // FIX: predicate
      };
      return _templater.Process("Where", dict);
    }

    string WhereExist(string query1, string query2, JoinOps joinop, bool not, bool twice) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "not", (x) => not ? "NOT" : "" },
        { "query1", (x) => query1 },
        { "query2", (x) => query2 },
        { "setop", (x) => _templater.JoinOp(joinop) },
      };
      return _templater.Process(twice ? "WhereExistAnd" : "WhereExist", dict);
    }

    public string CreateTable(string tablename, DataTable other) {
      var heading = (other.Heading.Degree == 0) ? DataHeading.Create("_dummy") : other.Heading;
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "table", (x) => tablename },
        { "coldefs", (x) => ColDefs(heading.Columns) },
        { "colnames", (x) => NameList(heading) },
      };
      return _templater.Process("Create", dict);
    }

    public string DropTable(string tablename) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "table", (x) => tablename },
      };
      return _templater.Process("Drop", dict);
    }

    public string Having(ExpressionBlock expr) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "expr", (x) => EvalFunc(expr) },  // FIX: predicate
      };
      return _templater.Process("Having", dict);
    }

    public string OrderBy(ExpressionBlock[] exprs) {
      if (exprs.Length == 0) return "";
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "ordcols", (x) => ColOrders(exprs) },
      };
      return _templater.Process("OrderBy", dict);
    }

    public string GroupBy(DataColumn[] cols) {
      if (cols.Length == 0) return "";
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "grpcols", (x) => NameList(DataHeading.Create(cols)) },
      };
      return _templater.Process("GroupBy", dict);
    }

    public string LimitOffset(int? limit, int? offset) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "~", (x) => (offset == null) ? "0" : (limit == null) ? "1" : "2" }, // pick a subtemplate
        { "limit", (x) => limit.ToString() },
        { "offset", (x) => offset.ToString() },
      };
      return _templater.Process("LimitOffset", dict);
    }

    public string SelectAs(string tableorquery, ExpressionBlock[] exprs) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select", (x) => tableorquery },
        { "namelist", (x) => NameAsList(exprs) },
      };
      return _templater.Process("SelectAs", dict);
    }

    public string SelectAsGroup(string tableorquery, ExpressionBlock[] exprs, DataColumn[] groups) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select", (x) => tableorquery },
        { "namelist", (x) => NameAsList(exprs) },
        { "groupby", (x) => GroupBy(groups) },
      };
      return _templater.Process("SelectAsGroup", dict);
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
      return _templater.Process("InsertValues", dict);
    }

    // Actually insert query results into this table
    public string InsertSelect(string tablename, string query) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "table", (x) => tablename },
        { "select", (x) => query },
      };
      return _templater.Process("InsertSelect", dict);
    }

    // Actually insert query results into this table
    public string InsertNamed(string tablename, DataHeading heading, string query) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "table", (x) => tablename },
        { "namelist", (x) => NameList(heading) },
        { "query", (x) => query },
      };
      return _templater.Process("InsertNamed", dict);
    }

    // Generate Sql to insert query results into this table
    public string InsertJoin(string name, DataHeading heading, string query, JoinOps joinop) {
      // ignore joinop for now
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "namelist", (x) => NameList(heading) },
        { "table", (x) => name },
        { "query", (x) => query },
      };
      return _templater.Process("InsertJoin", dict);
    }

    // Sql to insert rows of data into named table as literals (not currently used)
    //public string InsertValues(string tablename, DataTable other) {
    //  var namelist = NameList(other.Heading);
    //  var tmpl = _templater.Create("InsertValues");
    //  var rowno = 0;
    //  foreach (var row in other.GetRows()) {
    //    var values = row.Values;
    //    var valuelist = ValueList(other.Heading.Degree, x => ColumnValue(values[x]));
    //    var dict = new Dictionary<string, SubstituteDelegate> {
    //      { "table", (x) => tablename },
    //      { "namelist", (x) => namelist },
    //      { "valuelist", (x) => valuelist },
    //    };
    //    if (rowno > 0) tmpl.Append("\n");
    //    tmpl.Process(dict);
    //  }
    //  return tmpl.ToString();
    //}

    // Sql to delete according to predicate
    public string Delete(string tablename, ExpressionBlock pred) {
      var dict = new Dictionary<string, SubstituteDelegate> {
          { "table", (x) => tablename },
          { "pred", (x) => EvalFunc(pred) },
        };
      return _templater.Process("Delete", dict);
    }

    // Sql to delete multiple rows of data from named table with binding
    public string DeleteValues(string tablename, DataHeading other) {
      var names = other.Columns.Select(c => c.Name).ToArray();
      var dict = new Dictionary<string, SubstituteDelegate> {
          { "table", (x) => tablename },
          { "nameeqvaluelist", (x) => NameEqValueList(names) },
        };
      return _templater.Process("DeleteValues", dict);
    }

    // Sql to delete multiple rows of data using other table
    public string DeleteNamed(string tablename, DataHeading other, string query) {
      var namelist = NameList(other);
      var paramlist = ParamList(other);
      var dict = new Dictionary<string, SubstituteDelegate> {
          { "table", (x) => tablename },
          { "namelist", (x) => namelist },
          { "valuelist", (x) => paramlist },
        };
      return _templater.Process("DeleteValues", dict);
    }

    // Sql to udpate according to predicate and expressions
    public string Update(string tablename, ExpressionBlock pred, ExpressionBlock[] exprs) {
      var exps = exprs.Where(e => e.IsOpen).ToArray();
      var dict = new Dictionary<string, SubstituteDelegate> {
          { "table", (x) => tablename },
          { "pred", (x) => EvalFunc(pred) },
          { "namesetlist", (x) => NameSetList(exps) },
        };
      return _templater.Process("Update", dict);
    }

    public string SelectAll(string tableorquery, DataTableSql other) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select", (x) => tableorquery },
        { "namelist", (x) => NameList(other.Heading) },
      };
      return _templater.Process("SelectAll", dict);
    }

    // Using namelist, but nothing for empty heading
    string Using(DataHeading heading) {
      if (heading.Degree == 0) return "";
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "namelist", (x) => NameList(heading) },
      };
      return _templater.Process("Using", dict);
    }

    // Note: using <nameeqlist> triggers ambiguous column name
    public string SelectJoin(string tableorquery1, string tableorquery2, DataHeading newheading, DataHeading joinheading) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select1", (x) => tableorquery1 },
        { "select2", (x) => tableorquery2 },
        { "namelist", (x) => NameList(newheading) },
        { "join", (x) => joinheading.Degree == 0 ? "CROSS JOIN" : "INNER JOIN" },
        { "using", (x) => Using(joinheading) },
      };
      return _templater.Process("SelectJoin", dict);
    }

    public string SelectAntijoin(string tableorquery1, string tableorquery2, DataHeading newheading, DataHeading joinheading) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select1", (x) => tableorquery1 },
        { "select2", (x) => tableorquery2 },
        { "namelist", (x) => NameList(newheading) },
        { "nameeqlist", (x) => NameEqList(joinheading) },
      };
      return _templater.Process("SelectAntijoin", dict);
    }

    public string SelectSet(string tableorquery1, string tableorquery2, DataHeading newheading, JoinOps joinop) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select1", (x) => tableorquery1 },
        { "select2", (x) => tableorquery2 },
        { "namelist", (x) => NameList(newheading) },
        { "setop", (x) => _templater.JoinOp(joinop) },
      };
      return _templater.Process("SelectSetName", dict);
    }

    public string SelectOneWhere(string query1, string query2, JoinOps joinop, bool equal) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "whereexist", (x) => WhereExist(query1, query2, joinop, true, equal) },
      };
      return _templater.Process("SelectOneWhere", dict);
    }

    public string FullSelect(string select, string where, string order, string limit) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select", (x) => select },
        { "where", (x) => where },
        { "order", (x) => order },
        { "limit", (x) => limit },
      };
      return _templater.Process("FullSelect", dict);
    }

    public string SubSelect(string select) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select", (x) => select },
        { "alias", (x) => AliasName() },
      };
      return _templater.Process("SubSelect", dict);
    }

    public string SelectCount(string tableorquery) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        { "select", (x) => tableorquery },
      };
      return _templater.Process("SelectCount", dict);
    }

    public string CreateFunction(string name, DataColumn[] args, DataType retn) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        {  "name", (x) => name },
        {  "arglist", (x) => ColDefs(args) },
        {  "return", (x) => _templater.ColumnType(retn) },
        {  "body", (x) => name },
        {  "language", (x) => "plandl" },
      };
      return _templater.Process("Function", dict);
    }

    public string CreateAggregate(string name, DataColumn[] args, DataType statetype, TypedValue init, string sname, string fname) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        {  "name", (x) => name },
        {  "arglist", (x) => ColDefs(args) },
        {  "sfunc", (x) => sname },
        {  "stype", (x) => _templater.ColumnType(statetype) },
        {  "init", (x) => ColumnValue(init) },
        {  "ffunc", (x) => fname },
      };
      return _templater.Process("Aggregate", dict);
    }

    public string GetColumns(string name) {
      var dict = new Dictionary<string, SubstituteDelegate> {
        {  "tname", (x) => Quote(name) },
      };
      return _templater.Process("GetColumns", dict);
    }

  }
}
