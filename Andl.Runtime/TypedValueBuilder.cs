using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Andl.Runtime {
  public class ValueHolder {
    public DataType[] _types;
    public string[] _names;
    public TypedValue[] _values;
    public int _colidx = 0;
    public List<TypedValue[]> _list;
    public int _rowidx = 0;
    public ValueHolder _parent = null;

    public DataType DataType { get { return _types[_colidx]; } }
    bool InList { get { return _list != null; } }
  }

  /// <summary>
  /// Implement a build for a structured TypedValue
  /// 
  /// Initialised with a known datatype (which of course may contain RVA/TVA/UVA)
  /// Fill it one scalar value at a time, marking entry and exit of structured types
  /// </summary>
  public class TypedValueBuilder {

    ValueHolder _valueholder;

    public DataType[] DataTypes { get { return _valueholder._types; } }
    public string[] Names { 
      get { return _valueholder._names; } 
      set { _valueholder._names = value; } 
    }
    public int StructSize { get { return _valueholder._values.Length; } }
    public int ListSize { get { return _valueholder._list.Count; } }
    public TypedValue[] Values { get { return _valueholder._values; } }
    public bool Done { get { return _valueholder._rowidx >= _valueholder._list.Count; } }

    // Create a builder to receive values
    public static TypedValueBuilder Create(DataType[] types, string[] names = null) {
      return new TypedValueBuilder { 
        _valueholder = new ValueHolder {
          _types = types,
          _names = names ?? new string[types.Length],
          _values = new TypedValue[types.Length],
        },
      };
    }

    // Create a builder to emit values
    public static TypedValueBuilder Create(TypedValue[] values, string[] names = null) {
      return new TypedValueBuilder { 
        _valueholder = new ValueHolder {
          _types = values.Select(t => t.DataType).ToArray(),
          _names = names ?? new string[values.Length],
          _values = values,
        },
      };
    }

    public void SetBool(int colno, bool value) {
      _valueholder._values[colno] = BoolValue.Create(value);
    }

    public void SetNumber(int colno, decimal value) {
      _valueholder._values[colno] = NumberValue.Create(value);
    }

    public void SetTime(int colno, DateTime value) {
      _valueholder._values[colno] = TimeValue.Create(value);
    }

    public void SetText(int colno, string value) {
      _valueholder._values[colno] = TextValue.Create(value);
    }

    public void SetBinary(int colno, byte[] value) {
      _valueholder._values[colno] = BinaryValue.Create(value);
    }

    public void SetStructBegin(int colno) {
      _valueholder._colidx = colno;
      Logger.Assert(_valueholder.DataType.HasHeading);
      var cols = _valueholder._types[colno].Heading.Columns.ToArray();
      //var cols = _valueholder._types[colno].Heading.Columns.Select(c => c.DataType).ToArray();
      _valueholder = new ValueHolder {
        _types = cols.Select(c => c.DataType).ToArray(),
        _values = new TypedValue[cols.Length],
        _parent = _valueholder,
      };
    }

    public void SetStructEnd() {
      var tuple = _valueholder._values;
      _valueholder = _valueholder._parent;
      var datatype = _valueholder.DataType;
      if (datatype is DataTypeUser)
        _valueholder._values[_valueholder._colidx] = UserValue.Create(tuple, datatype as DataTypeUser);
      else if (datatype is DataTypeTuple) {
        var row = DataRow.Create(datatype.Heading, tuple);
        _valueholder._values[_valueholder._colidx] = TupleValue.Create(row);
      } else {
        Logger.Assert(_valueholder.DataType is DataTypeRelation);
        _valueholder._list.Add(tuple);
      }
    }

    public void SetListBegin(int colno) {
      _valueholder._colidx = colno;
      Logger.Assert(_valueholder.DataType is DataTypeRelation);
      _valueholder._list = new List<TypedValue[]>();
    }

    public void SetListEnd() {
      Logger.Assert(_valueholder.DataType is DataTypeRelation);
      var datatype = _valueholder.DataType;
      var rows = _valueholder._list.Select(t => DataRow.Create(_valueholder.DataType.Heading, t));
      var table = DataTableLocal.Create(_valueholder.DataType.Heading, rows);
      _valueholder._values[_valueholder._colidx] = RelationValue.Create(table);
      _valueholder._list = null;
    }


    public bool GetBool(int colno) {
      return (_valueholder._values[colno] as BoolValue).Value;
    }

    public decimal GetNumber(int colno) {
      return (_valueholder._values[colno] as NumberValue).Value;
    }

    public DateTime GetTime(int colno) {
      return (_valueholder._values[colno] as TimeValue).Value;
    }

    public string GetText(int colno) {
      return (_valueholder._values[colno] as TextValue).Value;
    }

    public byte[] GetBinary(int colno) {
      return (_valueholder._values[colno] as BinaryValue).Value;
    }

    public void GetStructBegin(int colno) {
      _valueholder._colidx = colno;
      Logger.Assert(_valueholder.DataType.HasHeading);
      var cols = _valueholder._types[colno].Heading.Columns;
      var value = _valueholder._values[colno];
      var tuple = (value is TupleValue) ? value.AsRow().Values 
                : (value is UserValue) ? value.AsUser()
                : _valueholder._list[_valueholder._rowidx++];
      _valueholder = new ValueHolder {
        _types = cols.Select(c => c.DataType).ToArray(),
        _names = cols.Select(c => c.Name).ToArray(),
        _values = tuple,
        _parent = _valueholder,
      };
    }

    public void GetStructEnd() {
      _valueholder = _valueholder._parent;
      Logger.Assert(_valueholder.DataType.HasHeading);
    }

    public void GetListBegin(int colno) {
      _valueholder._colidx = colno;
      Logger.Assert(_valueholder.DataType is DataTypeRelation);
      _valueholder._list = _valueholder._values[colno].AsTable().GetRows().Select(r => r.Values).ToList();
      _valueholder._rowidx = 0;
    }

    public void GetListEnd() {
      Logger.Assert(_valueholder.DataType is DataTypeRelation);
      _valueholder._list = null;
      _valueholder._rowidx = 0;
    }

  }

  public class TypedValueBuilderTest {
    TypedValueBuilder _input;
    TypedValueBuilder _output;

    // Call this with one or more values to self test
    public static void Test(params TypedValue[] values) {
      var tvbt = new TypedValueBuilderTest { _input = TypedValueBuilder.Create(values) };
      tvbt._output = TypedValueBuilder.Create(tvbt._input.DataTypes);
      tvbt.MoveData();
      for (var i = 0; i < values.Length; ++i)
        Logger.Assert(values[i].Equals(tvbt._output.Values[i]), "value test");
    }

    void MoveData() {
      for (var i = 0; i < _input.StructSize; ++i) {
        switch (_input.DataTypes[i].BaseName) {
        case "bool": _output.SetBool(i, _input.GetBool(i)); break;
        case "binary": _output.SetBinary(i, _input.GetBinary(i)); break;
        case "number": _output.SetNumber(i, _input.GetNumber(i)); break;
        case "date": // FIX: special handling until it's a subclass
        case "time": _output.SetTime(i, _input.GetTime(i)); break;
        case "text": _output.SetText(i, _input.GetText(i)); break;
        case "tuple": 
        case "user": 
          _input.GetStructBegin(i);
          _output.SetStructBegin(i);
          MoveData();
          _input.GetStructEnd();
          _output.SetStructEnd();
          break;
        case "relation": 
          _input.GetListBegin(i);
          _output.SetListBegin(i);
          while (!_input.Done) {
            _input.GetStructBegin(i);
            _output.SetStructBegin(i);
            MoveData();
            _input.GetStructEnd();
            _output.SetStructEnd();
          }
          _input.GetListEnd();
          _output.SetListEnd();
          break;
        default:
          Logger.Assert(false, _input.DataTypes[i].BaseName);
          break;
        }
      }
    }
  }
}
