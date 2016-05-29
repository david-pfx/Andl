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
using System.Data;
using System.Data.SqlClient;
using System.Data.OleDb;
using System.Data.Odbc;
using System.Data.Common;
using Andl.Common;

namespace Andl.Runtime {
  public enum ConversionTypes {
    None, Bool, Int, String, Decimal, DateTime
  };

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Subclass for Sql source type
  /// </summary>
  public class DataSourceSql : DataSourceSqlBase {
    SqlConnection _connection;

    public static DataSourceSql Create(string locator) {
      var ds = new DataSourceSql {
        _locator = locator,
      };
      try {
        ds._connection = new SqlConnection(locator);
      } catch(Exception ex) {
        throw ProgramError.Fatal("Source Sql", ex.Message);
      }
      ds._convdict = new Dictionary<string, ConversionTypes> {
        { "char", ConversionTypes.String },
        { "varchar", ConversionTypes.String },
        { "nchar", ConversionTypes.String },
        { "nvarchar", ConversionTypes.String },
        { "text", ConversionTypes.String },
        { "bit", ConversionTypes.Bool },
        { "int", ConversionTypes.Int },
        { "bigint", ConversionTypes.Int },
        { "smallint", ConversionTypes.Int },
        { "tinyint", ConversionTypes.Int },
        { "numeric", ConversionTypes.Decimal },
        { "decimal", ConversionTypes.Decimal },
        { "money", ConversionTypes.Decimal },
        { "smallmoney", ConversionTypes.Decimal },
        { "date", ConversionTypes.DateTime },
        { "datetime", ConversionTypes.DateTime },
        { "time", ConversionTypes.DateTime },
        { "datetime2", ConversionTypes.DateTime },
        { "smalldatetime", ConversionTypes.DateTime },
        { "datetimeoffset", ConversionTypes.DateTime },
      };

      return ds;
    }

    protected override DbDataReader Open(string table) {
      var cmd = new SqlCommand(String.Format("select * from {0}", table), _connection);
      try {
        _connection.Open();
      } catch (Exception ex) {
        throw ProgramError.Fatal("Source Sql", ex.Message);
      }
      return cmd.ExecuteReader();
    }

    protected override void Close() {
      _connection.Close();
    }

    protected override System.Data.DataTable GetSchema() {
      _connection.Open();
      var ret = _connection.GetSchema("tables");
      _connection.Close();
      return ret;
    }
  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Subclass for ODBC source type. 
  /// </summary>
  public class DataSourceOdbc : DataSourceSqlBase {
    OdbcConnection _connection;

    // Factory method
    public static DataSourceOdbc Create(string locator) {
      var ds = new DataSourceOdbc {
        _locator = locator,
      };
      try {
        ds._connection = new OdbcConnection(locator);
      } catch (Exception ex) {
        throw ProgramError.Fatal("Source Odbc", ex.Message);
      }
      ds._convdict = new Dictionary<string, ConversionTypes> {
        { "CHAR", ConversionTypes.String },
        { "VARCHAR", ConversionTypes.String },
        { "NCHAR", ConversionTypes.String },
        { "NVARCHAR", ConversionTypes.String },
        { "INTEGER", ConversionTypes.Int },
      };
      return ds;
    }

    //
    protected override DbDataReader Open(string table) {
      var cmd = new OdbcCommand(String.Format("select * from {0}", table), _connection);
      try {
        _connection.Open();
      } catch (Exception ex) {
        throw ProgramError.Fatal("Source Odbc", ex.Message);
      }
      return cmd.ExecuteReader();
    }

    protected override void Close() {
      _connection.Close();
    }

    protected override System.Data.DataTable GetSchema() {
      _connection.Open();
      var ret = _connection.GetSchema("tables");
      _connection.Close();
      return ret;
    }
  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Subclass for Oledb source type. Can read Access database.
  /// </summary>
  public class DataSourceOleDb : DataSourceSqlBase {
    OleDbConnection _connection;

    // Factory method
    public static DataSourceOleDb Create(string locator) {
      var ds = new DataSourceOleDb {
        _locator = locator,
      };
      try {
        ds._connection = new OleDbConnection(locator);
      } catch (Exception ex) {
        throw ProgramError.Fatal("Source OleDb", ex.Message);
      }
      ds._convdict = new Dictionary<string, ConversionTypes> {
        { "DBTYPE_BOOL", ConversionTypes.Bool },
        { "DBTYPE_I4", ConversionTypes.Int },
        { "DBTYPE_DATE", ConversionTypes.DateTime },
        { "DBTYPE_WVARCHAR", ConversionTypes.String },
        { "DBTYPE_WVARLONGCHAR", ConversionTypes.String },
      };
      ds._schemadict = new Dictionary<string, ConversionTypes> {
        { "_TABLE", ConversionTypes.Bool },
      };
      return ds;
    }

    //
    protected override DbDataReader Open(string table) {
      var cmd = new OleDbCommand(String.Format("select * from {0}", table), _connection);
      try {
        _connection.Open();
      } catch (Exception ex) {
        throw ProgramError.Fatal("Source OleDb", ex.Message);
      }
      return cmd.ExecuteReader();
    }

    protected override void Close() {
      _connection.Close();
    }

    protected override System.Data.DataTable GetSchema() {
      _connection.Open();
      var ret = _connection.GetSchema("tables");
      _connection.Close();
      return ret;
    }
  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Base class for SQL readers providing common code
  /// </summary>
  public abstract class DataSourceSqlBase : DataSourceStream {
    protected Dictionary<string, ConversionTypes> _convdict;
    protected Dictionary<string, ConversionTypes> _schemadict;
    //protected Dictionary<string, DataType> _typedict;

    // open a table and return a reader
    abstract protected DbDataReader Open(string table);
    // close the table
    abstract protected void Close();
    // get schema info
    abstract protected System.Data.DataTable GetSchema();

    // Generic input
    public override DataHeading Peek(string table) {
      Logger.WriteLine(2, "Sql Peek '{0}'", table);
      if (table == "*") return Peek();
      var reader = Open(table);
      var s = Enumerable.Range(0, reader.FieldCount)
        .Select(n => reader.GetName(n) + ":" + reader.GetDataTypeName(n)).ToArray();
      Logger.WriteLine(3, "Table {0} fields {1}", table, String.Join(",", s));
      var cols = Enumerable.Range(0, reader.FieldCount)
        .Where(x => _convdict.ContainsKey(reader.GetDataTypeName(x)))
        .Select(x => DataColumn.Create(reader.GetName(x), GetType(_convdict[reader.GetDataTypeName(x)])))
        .ToArray();
      Close();
      return DataHeading.Create(cols, false); // preserve order
    }

    public override DataTable Read(string table, DataHeading heading) {
      Logger.WriteLine(2, "Sql Read '{0}'", table);
      if (table == "*") return Read(heading);
      var tabnew = DataTableLocal.Create(heading);
      for (var reader = Open(table); reader.Read(); ) {
        var values = heading.Columns.Select(c => MakeValue(reader, c.Name, c.DataType)).ToArray();
        var row = DataRow.Create(heading, values);
        tabnew.AddRow(row);
      }
      Close();
      return tabnew;
    }

    Dictionary<Type, DataType> _type2datatype = new Dictionary<Type, DataType> {
      { typeof(string), DataTypes.Text },
      { typeof(int), DataTypes.Number },
      { typeof(decimal), DataTypes.Number },
      { typeof(DateTime), DataTypes.Time },
    };

    DataHeading Peek() {
      var schema = GetSchema();
      var scols = schema.Columns;
      var cols = Enumerable.Range(0, scols.Count)
        .Select(x => DataColumn.Create(scols[x].ColumnName,
          _type2datatype.ContainsKey(scols[x].DataType) ? _type2datatype[scols[x].DataType] : DataTypes.Text))
        .ToArray();
      return DataHeading.Create(cols);
    }

    DataTableLocal Read(DataHeading heading) {
      var schema = GetSchema();
      var scols = schema.Columns;
      var rows = schema.Rows;
      var newtab = DataTableLocal.Create(heading);
      foreach (System.Data.DataRow row in rows) {
        var values = Enumerable.Range(0, scols.Count)
          .Select(x => TypedValue.Convert(heading.Columns[x].DataType, row.IsNull(x) ? null : row[x]))
          .ToArray();
        var newrow = DataRow.Create(heading, values);
        newtab.AddRow(newrow);
      }
      return newtab;
    }

    private DataColumn MakeColumn(string name, string datatype) {
      if (_convdict.ContainsKey(datatype))
        return DataColumn.Create(name, GetType(_convdict[datatype]));
      else return DataColumn.Create(name, DataTypes.Void);      
    }

    // Make a value of the desired type
    TypedValue MakeValue(DbDataReader rdr, string fieldname, DataType datatype) {
      var field = rdr.GetOrdinal(fieldname);
      if (rdr.IsDBNull(field))
        return datatype.DefaultValue();
      var typename = rdr.GetDataTypeName(field);
      var value = rdr.GetValue(field);
      return (_convdict.ContainsKey(typename)) ? Converter(_convdict[typename], value) 
        : TypedValue.Empty;
    }

    // Converter driven by source type
    TypedValue Converter(ConversionTypes type, object value) {
      switch (type) {
      case ConversionTypes.Bool: return BoolValue.Create((bool)value);
      case ConversionTypes.Int:return NumberValue.Create((int)value);
      case ConversionTypes.String: return TextValue.Create((string)value);
      case ConversionTypes.Decimal: return NumberValue.Create((decimal)value);
      case ConversionTypes.DateTime: return TimeValue.Create((DateTime)value);
      default: return TypedValue.Empty;
      }
    }

    DataType GetType(ConversionTypes type) {
      switch (type) {
      case ConversionTypes.Bool: return DataTypes.Bool;
      case ConversionTypes.Int:return DataTypes.Number;
      case ConversionTypes.String: return DataTypes.Text;
      case ConversionTypes.Decimal:return DataTypes.Number;
      case ConversionTypes.DateTime:return DataTypes.Time;
      default: return DataTypes.Unknown;
      }
    }

  }
}
