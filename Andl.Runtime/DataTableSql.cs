/// Andl is A New Data Language. See andl.org.
///
/// Copyright © David M. Bennett 2015 as an unpublished work. All rights reserved.
///
/// If you have received this file directly from me then you are hereby granted 
/// permission to use it for personal study. For any other use you must ask my 
/// permission. Not to be copied, distributed or used commercially without my 
/// explicit written permission.
///
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Andl.Sqlite;

namespace Andl.Runtime {
  public delegate string SubstituteDelegate(int index);

  /// <summary>
  /// Implement an SQL interface for a data table
  /// 
  /// Core function is to generate SQL and in due course allow it to be executed
  /// </summary>
  public class DataTableSql : DataTable {
    static DataHeading SqlDummyHeading = DataHeading.Create("_dummy_:text");

    // The table name if it has one (only if BaseTable true)
    public string TableName { get; private set; }
    // Sql for SELECT query
    public string SqlSelectText { get; private set; }
    // Sql for CREATE
    public string SqlCreateText { get; private set; }
    // Sql for WHERE clause
    public string SqlWhereText { get; private set; }
    // Sql for ORDERED BY clause
    public string SqlOrderByText { get; private set; }
    // True if this is a base table, otherwise it's just a query
    public bool BaseTable { get { return TableName != null; } }

    // Database, could be different for different back ends
    SqlTarget _database;
    // Sql generator, could be different for different back ends
    SqlGen _gen;

    // Return sql for a bare SELECT clause
    string GetSelect() {
      return BaseTable ? _gen.SelectAll(TableName, this) : SqlSelectText;
    }

    // Return sql for a full query: SELECT WHERE ORDERBY
    string GetQuery() {
      return String.Join(" ", GetSelect(), SqlWhereText, SqlOrderByText);
    }

    // return sql for use in a FROM clause
    string GetFrom() {
      return BaseTable ? '[' + TableName + ']' : "( " + GetQuery() + ")";
    }

    //--- overrides

    // Return the Local flag
    public override bool IsLocal { get { return false; } }

    // Test two tables for equality!
    public override bool Equals(object obj) {
      return obj is DataTable && IsEqual(obj as DataTable);
    }

    public override int GetHashCode() {
      return Heading.GetHashCode();
    }

    public override string ToString() {
      var text = GetQuery().Trim();
      return String.Format("{{{0}<{1}>}}", Heading, text.Length <= 80 ? text : text.Substring(0, 80) + "...");
    }

    public override string Format() {
      var text = GetQuery().Trim();
      return String.Format("{{{0}<{1}>}}", Heading, text);
    }

    public override IEnumerable<DataRow> GetRows() {
      _database.OpenStatement();
      _database.ExecuteQuery(GetQuery());
      while (_database.HasData) {
        var values = new TypedValue[Degree];
        _database.GetData(Heading, values);
        yield return DataRow.Create(Heading, values);
        _database.Fetch();
      }
      _database.CloseStatement();
    }

    public override void DropRows() {
      _database.CloseStatement();
    }

    //--- factories

    DataTableSql() {
      _database = SqlTarget.Create();
      _gen = SqlTarget.SqlGen;
    }

    // Create new base table, assumed to exist
    public static DataTableSql Create(string name, DataHeading heading) {
      return new DataTableSql {
        Heading = heading,
        TableName = name,
      };
    }

    // Create new virtual table based on a query
    static DataTableSql CreateFromSql(DataHeading heading, string sql, string ord = null) {
      var newtable = new DataTableSql {
        Heading = heading,
        SqlSelectText = sql,
        SqlOrderByText = ord,
      };
      return newtable;
    }

    // Create new virtual table based on an existing table/query
    static DataTableSql CreateFromSql(DataTableSql table) {
      var newtable = new DataTableSql {
        Heading = table.Heading,
        SqlSelectText = table.GetQuery(),
      };
      //newtable._lookups.AddRange(table._lookups);
      return newtable;
    }

    // Create new base table from relation value and populate it
    public static DataTableSql Create(string name, DataTable other) {
      var oldtable = other as DataTableSql;
      // cannot copy base table to itself
      if (oldtable != null && name == oldtable.TableName) return oldtable;
      var newtable = Create(name, other.Heading);
      newtable._database.Begin();
      newtable.CreateTable();
      if (oldtable != null)
        newtable.InsertValuesQuery(oldtable);
      else
        newtable.InsertValuesSingly(other);
      newtable._database.Commit();
      return newtable;
    }

    // Create new table by converting another
    // NOTE: receiver not used, but allows desired dispatch
    public static DataTableSql Convert(DataTable other) {
      if (other is DataTableSql) return other as DataTableSql;
      var name = SqlTarget.SqlGen.TempName();
      var newtable = Create(name, other.Heading);
      newtable._database.Begin();
      newtable.CreateTable();                   //FIX: put temp in memory db
      newtable.InsertValuesSingly(other);
      newtable._database.Commit();
      return newtable;
    }

    // Create new table by converting another
    // NOTE: receiver not used, but allows desired dispatch
    public override DataTable ConvertWrap(DataTable other) {
      return Convert(other);
    }

    //--- internal functions

    //--- functions to execute queries

    // Get a single unnamed scalar value
    int GetIntValue(string sql) {
      _database.OpenStatement();
      _database.ExecuteQuery(sql);
      Logger.Assert(_database.HasData);
      int value;
      _database.GetData(out value);
      _database.CloseStatement();
      return value;
    }

    // Get a single unnamed scalar value
    bool GetBoolValue(string sql) {
      _database.OpenStatement();
      _database.ExecuteQuery(sql);
      bool value;
      _database.GetData(out value);
      _database.CloseStatement();
      return value;
    }

    // Create new base table
    void CreateTable() {
      _database.OpenStatement();
      var sql = _gen.CreateTable(TableName, this);
      _database.ExecuteCommand(sql);
      _database.CloseStatement();
    }

    // Sql to insert multiple rows of data into named table
    void InsertValuesSingly(DataTable other) {
      _database.OpenStatement();
      var sql = _gen.InsertValuesParam(TableName, other);
      _database.ExecutePrepare(sql);
      foreach (var row in other.GetRows())
        _database.ExecuteSend(row.Values);
      _database.CloseStatement();
    }

    // Sql to insert multiple rows of data into named table
    void InsertValuesQuery(DataTableSql other) {
      _database.OpenStatement();
      // SQL does positional matching, so column order must come from other
      var sql = _gen.InsertNamed(TableName, other.Heading, other.GetQuery()); //WRONG?
      _database.ExecuteCommand(sql);
      _database.CloseStatement();
    }

    //--- functions to manipulate queries

    // generate SQL code for restrict
    DataTableSql AddWhere(ExpressionBlock expr) {
      var sql = _gen.Where(expr);
      var newtable = DataTableSql.CreateFromSql(this);
      // TODO: add WHERE to existing statement if there is none
      //var newtable = (!BaseTable && SqlWhereText == null) ? this : DataTableSql.CreateFromSql(this);
      newtable.SqlWhereText = sql;
      return newtable;
    }

    // generate SQL code for order by
    DataTableSql AddOrderBy(ExpressionBlock[] expr) {
      var sql = _gen.OrderBy(expr);
      var newtable = (SqlOrderByText == null) ? this : DataTableSql.CreateFromSql(this);
      newtable.SqlOrderByText = sql;
      return newtable;
    }

    // generate SQL code for dyadic join operations
    DataTableSql DyadicJoin(DataTableSql other, DataHeading joinhdg, DataHeading newheading) {
      var sql = _gen.SelectJoin(GetFrom(), other.GetFrom(), newheading, joinhdg);
      var newtable = DataTableSql.CreateFromSql(newheading, sql);
      return newtable;
    }

    // generate SQL code for dyadic antijoin operations
    DataTableSql DyadicAntijoin(DataTableSql other, DataHeading joinhdg, DataHeading newheading) {
      var sql = _gen.SelectAntijoin(GetFrom(), other.GetFrom(), newheading, joinhdg);
      var newtable = DataTableSql.CreateFromSql(newheading, sql);
      return newtable;
    }

    // generate SQL code for dyadic set operations
    DataTableSql DyadicSet(DataTableSql other, DataHeading newheading, JoinOps joinop) {
      var sql = _gen.SelectSet(GetFrom(), other.GetFrom(), newheading, joinop);
      var newtable = DataTableSql.CreateFromSql(newheading, sql);
      return newtable;
    }

    // Generate SQL for COUNT
    DataTableSql GenCount() {
      var sql = _gen.SelectCount(GetFrom());
      var newtable = DataTableSql.CreateFromSql(DataHeading.Empty, sql);
      return newtable;
    }

    // Compare tables: joinop is INTERSECT, MINUS or NUL (for equals)
    bool SetCompare(DataTableSql other, JoinOps joinop, bool both) {
      Logger.Assert(Heading.Equals(other.Heading));
      var sql = _gen.SelectOneWhere(GetQuery(), other.GetQuery(), joinop, both);
      var ret = GetBoolValue(sql);
      return ret;
    }

    //--- publics

    //--- expressions with scalar result

    // Count is COUNT(*)
    public override int GetCount() {
      var newtable = GenCount();
      var ret = GetIntValue(newtable.GetQuery());
      Logger.WriteLine(4, "[Count '{0}']", ret);
      return ret;
    }

    // A eql B means A minus B is empty both ways
    public override bool IsEqual(DataTable otherarg) {
      var ret = SetCompare(Convert(otherarg), JoinOps.MINUS, true);
      Logger.WriteLine(4, "[Eql '{0}']", ret);
      return ret;
    }

    // A sub B means A minus B is empty
    public override bool Subset(DataTable otherarg) {
      var ret = SetCompare(Convert(otherarg), JoinOps.MINUS, false);
      Logger.WriteLine(4, "[Sub '{0}']", ret);
      return ret;
    }

    // A sup B means B minus A is empty
    public override bool Superset(DataTable otherarg) {
      var ret = Convert(otherarg).SetCompare(this, JoinOps.MINUS, false);
      Logger.WriteLine(4, "[Sup '{0}']", ret);
      return ret;
    }

    // A sep B means A intersect B is empty
    public override bool Separate(DataTable otherarg) {
      var ret = SetCompare(Convert(otherarg), JoinOps.INTERSECT, false);
      Logger.WriteLine(4, "[Sep '{0}']", ret);
      return ret;
    }

    //--- monadics

    public override TypedValue Lift() {
      if (!(Degree > 0)) return TypedValue.Empty;
      var row = GetRows().FirstOrDefault();
      DropRows();
      return row != null ? row.Values[0] : Heading.Columns[0].DataType.Default();
    }

    public override DataTable Project(ExpressionBlock[] exprs) {
      var heading = DataHeading.Create(exprs);
      var sql = _gen.SelectAs(GetFrom(), exprs);
      var newtable = DataTableSql.CreateFromSql(heading, sql);
      Logger.WriteLine(4, "[Pro '{0}']", newtable);
      return newtable;
    }

    public override DataTable Rename(ExpressionBlock[] exprs) {
      // note: heading order must match exprs (cf Local)
      var heading = DataHeading.Create(exprs);
      var sql = _gen.SelectAs(GetFrom(), exprs);
      var newtable = DataTableSql.CreateFromSql(heading, sql);
      Logger.WriteLine(4, "[Ren '{0}']", newtable);
      return newtable;
    }

    public override DataTable Restrict(ExpressionBlock expr) {
      _database.RegisterExpressions(expr);
      var newtable = AddWhere(expr);
      Logger.WriteLine(4, "[Res '{0}']", newtable);
      return newtable;
    }

    public override DataTable Transform(DataHeading heading, ExpressionBlock[] exprs) {
      _database.RegisterExpressions(exprs);
      var sql = _gen.SelectAs(GetFrom(), exprs);
      var newtable = DataTableSql.CreateFromSql(heading, sql);
      Logger.WriteLine(4, "[Trn '{0}']", newtable);
      return newtable;
    }

    public override DataTable TransformAggregate(DataHeading heading, ExpressionBlock[] exprs) {
      _database.RegisterExpressions(exprs);
      var sql = _gen.SelectAsGroup(GetFrom(), exprs);
      var newtable = DataTableSql.CreateFromSql(heading, sql);
      Logger.WriteLine(4, "[TrnA '{0}']", newtable);
      return newtable;
    }

    public override DataTable TransformOrdered(DataHeading heading, ExpressionBlock[] exprs, ExpressionBlock[] orderexps) {
      _database.RegisterExpressions(exprs);
      var sql = _gen.SelectAsGroup(GetFrom(), exprs);
      var ord = _gen.OrderBy(orderexps);
      var newtable = DataTableSql.CreateFromSql(heading, sql, ord);
      Logger.WriteLine(4, "[TrnO '{0}']", newtable);
      return newtable;
    }

    //--- dyadics

    public override DataTable DyadicJoin(DataTable otherarg, JoinOps joinops, DataHeading newheading) {
      var other = Convert(otherarg);
      var joinhdg = Heading.Intersect(other.Heading);
      DataTableSql newtable;
      if (joinops.HasFlag(JoinOps.REV))
        newtable = (other).DyadicJoin(this, joinhdg, newheading);
      else newtable = DyadicJoin(other, joinhdg, newheading);
      Logger.WriteLine(4, "[DJ '{0}']", newtable);
      return newtable;
    }

    public override DataTable DyadicAntijoin(DataTable otherarg, JoinOps joinops, DataHeading newheading) {
      var other = Convert(otherarg);
      var joinhdg = Heading.Intersect(other.Heading);
      DataTableSql newtable;
      if (joinops.HasFlag(JoinOps.REV))
        newtable = (other).DyadicAntijoin(this, joinhdg, newheading);
      else newtable = DyadicAntijoin(other, joinhdg, newheading);
      Logger.WriteLine(4, "[DAJ '{0}']", newtable);
      return newtable;
    }

    public override DataTable DyadicSet(DataTable otherarg, JoinOps joinop, DataHeading newheading) {
      var other = Convert(otherarg);
      DataTableSql newtable;
      if (joinop == JoinOps.RMINUS)
        newtable = other.DyadicSet(this, newheading, JoinOps.MINUS);
      else if (joinop == JoinOps.SYMDIFF) {
        var r1 = DyadicSet(other, newheading, JoinOps.MINUS);
        var r2 = other.DyadicSet(this, newheading, JoinOps.MINUS);
        newtable = r1.DyadicSet(r2, newheading, JoinOps.UNION);
      }  else newtable = DyadicSet(other, newheading, joinop);
      Logger.WriteLine(4, "[DS '{0}']", newtable);
      return newtable;
    }

    //--- updates

    public override DataTable UpJoin(DataTable other, JoinOps joinops) {
      if (other is DataTableSql)
        InsertValuesQuery(other as DataTableSql);
      else InsertValuesSingly(other);
      //var other = Convert(otherarg);
      //_database.OpenStatement();
      //string sql = _gen.InsertJoin(TableName, other.Heading, other.GetQuery(), joinops);
      //_database.ExecuteCommand(sql);
      //_database.CloseStatement();
      return this;
    }

    public override DataTable UpdateTransform(ExpressionBlock pred, ExpressionBlock[] exprs) {
      _database.RegisterExpressions(pred);
      _database.RegisterExpressions(exprs);
      if (exprs.Length == 0) {
        string sql = _gen.Delete(TableName, pred);
        _database.OpenStatement();
        _database.ExecuteCommand(sql);
        _database.CloseStatement();
      } else {
        string sql = _gen.Update(TableName, pred, exprs);
        _database.OpenStatement();
        _database.ExecuteCommand(sql);
        _database.CloseStatement();
      }
      return this;
    }

    public override DataTable Recurse(int flags, ExpressionBlock expr) {
      throw new NotImplementedException();
    }
  }
}
