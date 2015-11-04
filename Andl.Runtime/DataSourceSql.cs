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
using System.Data;
using System.Data.SqlClient;
using System.Data.OleDb;
using System.Data.Odbc;
using System.Data.Common;

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
        ProgramError.Fatal("Source sql", ex.Message);
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
      _connection.Open();
      return cmd.ExecuteReader();
    }

    protected override void Close() {
      _connection.Close();
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
        ProgramError.Fatal("Source odbc", ex.Message);
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
      _connection.Open();
      return cmd.ExecuteReader();
    }

    protected override void Close() {
      _connection.Close();
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
        ProgramError.Fatal("Source OleDb", ex.Message);
      }
      ds._convdict = new Dictionary<string, ConversionTypes> {
        { "DBTYPE_BOOL", ConversionTypes.Bool },
        { "DBTYPE_I4", ConversionTypes.Int },
        { "DBTYPE_DATE", ConversionTypes.DateTime },
        { "DBTYPE_WVARCHAR", ConversionTypes.String },
        { "DBTYPE_WVARLONGCHAR", ConversionTypes.String },
      };
      return ds;
    }

    //
    protected override DbDataReader Open(string table) {
      var cmd = new OleDbCommand(String.Format("select * from {0}", table), _connection);
      _connection.Open();
      return cmd.ExecuteReader();
    }

    protected override void Close() {
      _connection.Close();
    }
  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Base class for SQL readers providing common code
  /// </summary>
  public abstract class DataSourceSqlBase : DataSourceStream {
    protected Dictionary<string, ConversionTypes> _convdict;
    //protected Dictionary<string, DataType> _typedict;

    // open a table and return a reader
    abstract protected DbDataReader Open(string table);
    // close the table
    abstract protected void Close();

    // Generic input
    public override DataTable Input(string table, InputMode mode = InputMode.Import) {
      Logger.WriteLine(2, "Table '{0}' mode:{1}", table, mode);
      var reader = Open(table);
      var s = Enumerable.Range(0, reader.FieldCount)
        .Select(n => reader.GetName(n) + ":" + reader.GetDataTypeName(n)).ToArray();
      Logger.WriteLine(3, "Table {0} fields {1}", table, String.Join(",", s));
      var cols = Enumerable.Range(0, reader.FieldCount)
        .Where(x => _convdict.ContainsKey(reader.GetDataTypeName(x)))
        .Select(x => DataColumn.Create(reader.GetName(x), GetType(_convdict[reader.GetDataTypeName(x)])))
        .ToArray();
      var heading = DataHeading.Create(cols, false); // preserve order
      var tabnew = DataTableLocal.Create(heading);
      while (reader.Read() && mode == InputMode.Import) {
        var values = cols.Select(c => MakeValue(reader, c.Name, c.DataType)).ToArray();
        var row = DataRow.Create(heading, values);
        tabnew.AddRow(row);
      }
      Close();
      return tabnew;
    }

    protected delegate TypedValue converter(object ValueType);

    delegate TypedValue CreateValueDelegate(SqlDataReader rdr, int x);

    // function to create typed value from column value of given type
    Dictionary<DataType, CreateValueDelegate> _createdict = new Dictionary<DataType,CreateValueDelegate>() {
      { DataTypes.Bool, (r, x) => TypedValue.Create(r.GetBoolean(x)) },
      { DataTypes.Number, (r, x) => TypedValue.Create(r.GetDecimal(x)) },
      { DataTypes.Time, (r, x) => TypedValue.Create(r.GetDateTime(x)) },
      { DataTypes.Text, (r, x) => TypedValue.Create(r.GetString(x)) },
    };

    // values to use instead of SQL NULL
    Dictionary<DataType, TypedValue> _nulldict = new Dictionary<DataType, TypedValue>() {
      { DataTypes.Bool, BoolValue.Default },
      { DataTypes.Number, NumberValue.Default },
      { DataTypes.Time, TimeValue.Default },
      { DataTypes.Text, TextValue.Default },
    };

    private DataColumn MakeColumn(string name, string datatype) {
      if (_convdict.ContainsKey(datatype))
        return DataColumn.Create(name, GetType(_convdict[datatype]));
      else return DataColumn.Create(name, DataTypes.Void);      
    }

    TypedValue MakeValue(DbDataReader rdr, string fieldname, DataType datatype) {
      var field = rdr.GetOrdinal(fieldname);
      if (rdr.IsDBNull(field))
        return _nulldict[datatype];
      var typename = rdr.GetDataTypeName(field);
      var value = rdr.GetValue(field);
      if (_convdict.ContainsKey(typename))
        return Converter(_convdict[typename],value);
      return TypedValue.Empty;
    }

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
