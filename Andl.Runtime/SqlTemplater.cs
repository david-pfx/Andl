using System.Collections.Generic;
using Andl.Common;

namespace Andl.Runtime {
  /// <summary>
  /// Base class with generic SQL, override for individual dialect
  /// </summary>
  abstract public class SqlTemplater : Templater {
    // subclass to initialise
    protected Dictionary<DataType, string> _datatypetosqldict;

    readonly Dictionary<JoinOps, string> _joinoptosql = new Dictionary<JoinOps, string> {
      { JoinOps.UNION, "UNION" },
      { JoinOps.INTERSECT, "INTERSECT" },
      { JoinOps.MINUS, "EXCEPT" },
    };
    const string Ascending = "ASC";
    const string Descending = "DESC";

    // common base mostly standard -- override for individual dialects
    readonly Dictionary<string, string> _templatedict = new Dictionary<string, string> {
      { "Create",         "DROP TABLE IF EXISTS <!table> \n" +
                          "CREATE TABLE <!table> ( <coldefs>, UNIQUE ( <colnames> ) )" },
                          //"CREATE TABLE <!table> ( <coldefs>, UNIQUE ( <colnames> ) ON CONFLICT IGNORE )" },
      { "Drop",           "DROP TABLE <!table>" },
      { "SelectAll",      "SELECT <namelist> FROM <#select>" },
      { "SelectAs",       "SELECT DISTINCT <namelist> FROM <#select>" },
      { "SelectAsGroup",  "SELECT DISTINCT <namelist> FROM <#select> <groupby>" },
      { "SelectJoin",     "SELECT DISTINCT <namelist> FROM <#select1> <join> <#select2> <using>" },
      { "SelectAntijoin", "SELECT DISTINCT <namelist> FROM <@select1> _a_ WHERE NOT EXISTS (SELECT 1 FROM <@select2> _b_ WHERE <nameeqlist>)" },
      { "SelectSet",      "<#select1> <setop> <#select2>" },
      { "SelectSetName",  "SELECT DISTINCT <namelist> FROM <#select1> <setop> SELECT <namelist> FROM <#select2>" },
      { "SelectCount",    "SELECT COUNT(*) FROM <#select>" },
      { "SelectOneWhere", "SELECT 1 <whereexist>" },
      { "FullSelect",     "<select> <where> <order> <limit>" },
      { "SubSelect",      "( <select> )" },

      { "InsertNamed",    "INSERT INTO <!table> ( <namelist> ) <query>" },
      { "InsertSelect",   "INSERT INTO <!table> <query>" },
      { "InsertValues",   "INSERT INTO <!table> ( <namelist> ) VALUES ( <valuelist> )" },
      { "InsertJoin",     "INSERT INTO <!table> ( <namelist> ) <query>" },
      { "Delete",         "DELETE FROM <!table> WHERE <pred>" },
      { "DeleteValues",   "DELETE FROM <!table> WHERE <nameeqvaluelist>" },
      { "DeleteExist",    "DELETE FROM <!table> _a_ WHERE EXISTS (SELECT 1 FROM <@select2> _b_ WHERE <nameeqlist>)" },
      { "Update",         "UPDATE <!table> SET <namesetlist> WHERE <pred>" },

      { "WhereExist",     "WHERE <not> EXISTS ( <query1> <setop> <query2> )" },
      { "WhereExistAnd",  "WHERE <not> EXISTS ( <query1> <setop> <query2> ) AND <not> EXISTS ( <query2> <setop> <query1> )" },
      { "Where",          "WHERE <expr>" },
      { "Having",         "HAVING <expr>" },
      { "Using",          "USING ( <namelist> )" },

      { "Function",       null },
      { "Aggregate",      null },
      { "GetColumns",     null },

      { "OrderBy",        "ORDER BY <ordcols>" },
      { "GroupBy",        "GROUP BY <grpcols>" },
      { "LimitOffset",    "LIMIT <limit>~OFFSET <offset>~OFFSET <offset> LIMIT <limit>" },
      { "EvalFunc",       "<!func>(<lookups>)" },
      { "Coldef",         "<!colname> <coltype>" },
      { "Name",           "<!name1>" },
      { "NameEq",         "_a_.<!name1> = _b_.<!name1>" },
      { "NameEqValue",    "<!name1> = <value>" },
      { "NameAs",         "<!name1>~<!name2> AS <!name1>~<expr> AS <!name1>" },
      { "NameSet",        "~~<!name1> = <expr>" },
      { "Value",          "<value>" },
      { "Param",          "?<param>" },
    };

    protected SqlTemplater() : base() {
      _templatedicts.Insert(0, _templatedict);
    }

    public virtual string SortOrder(bool descending) {
      return descending ? Descending : Ascending;
    }

    // return the corresponding Sql type 
    public virtual string ColumnType(DataType datatype) {
      Logger.Assert(_datatypetosqldict.ContainsKey(datatype.BaseType), datatype);
      return _datatypetosqldict[datatype.BaseType];
    }

    public virtual string JoinOp(JoinOps joinop) {
      Logger.Assert(_joinoptosql.ContainsKey(joinop), joinop);
      return _joinoptosql[joinop];
    }

  }
  /// <summary>
  /// Implement templater for Sqlite
  /// </summary>
  public class SqliteTemplater : SqlTemplater {
    static Dictionary<string, string> _templatedict = new Dictionary<string, string> {
      { "LimitOffset",    "LIMIT <limit>~LIMIT -1 OFFSET <offset>~LIMIT <limit> OFFSET <offset>" },
      { "Param",          "?<param>" },
    };

    readonly Dictionary<DataType, string> _dtdict = new Dictionary<DataType, string> {
      { DataTypes.Binary, "BLOB" },
      { DataTypes.Bool, "BOOLEAN" },
      { DataTypes.Number, "TEXT" },
      { DataTypes.Text, "TEXT" },
      { DataTypes.Time, "TEXT" },
      { DataTypes.Table, "BLOB" },
      { DataTypes.Row, "BLOB" },
      { DataTypes.User, "BLOB" },
    };

    public SqliteTemplater() : base() {
      _templatedicts.Insert(0, _templatedict);
      _datatypetosqldict = _dtdict;
    }
  }

  /// <summary>
  /// Implement templater for Postgres
  /// </summary>
  public class PostgresTemplater : SqlTemplater {
    Dictionary<string, string> _templatedict = new Dictionary<string, string> {
      { "Function",       "CREATE OR REPLACE FUNCTION <!name>( <arglist> ) RETURNS <return> AS '<body>' LANGUAGE <language>" },
      { "Aggregate",      "DROP AGGREGATE IF EXISTS <!name>( <arglist> ) \n" +
                          "CREATE AGGREGATE <!name>( <arglist> ) ( SFUNC = <!sfunc>, STYPE = <stype>, "
                          +"INITCOND = <init>, FINALFUNC = <!ffunc> )" },
      { "GetColumns",     "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = <tname>" },
      { "Param",          "$<param>" },
    };

    readonly Dictionary<DataType, string> _dtdict = new Dictionary<DataType, string> {
      { DataTypes.Binary, "BYTEA" },
      { DataTypes.Bool, "BOOLEAN" },
      { DataTypes.Number, "NUMERIC" },
      { DataTypes.Text, "TEXT" },
      { DataTypes.Time, "TIMESTAMP" },
      { DataTypes.Table, "BYTEA" },
      { DataTypes.Row, "BYTEA" },
      { DataTypes.User, "BYTEA" },
    };

    public PostgresTemplater() : base() {
      _templatedicts.Insert(0, _templatedict);
      _datatypetosqldict = _dtdict;
    }
  }
}
