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

namespace Andl.Runtime {

  ///-------------------------------------------------------------------
  /// <summary>
  /// A data value must have a type and compare equal.
  /// </summary>
  public interface IDataValue {
    DataType DataType { get; }
    //bool Equal(object other);
  }

  public interface IOrderedValue : IDataValue, IComparable {
    bool IsLess(object other);
  }

  public interface IOrdinalValue : IOrderedValue {
    //bool IsLess(object other);
    IOrdinalValue Maximum();
    IOrdinalValue Minimum();
  }
  
  ///-------------------------------------------------------------------
  /// <summary>
  /// Base class for typed values
  /// </summary>
  public abstract class TypedValue : IDataValue {
    // must return allocated datatype
    public abstract DataType DataType { get; }
    // return heading, if there is one
    public virtual DataHeading Heading { get { return null; } }
    // Empty will turn out to be a binary value of length zero
    public static TypedValue Empty { get { return BinaryValue.Default; } }

    public int MinLength { get { return 8; } }

    public abstract string Format();

    //public override string ToString() {
    //  return Format();
    //}

    public string AsString() {
      return (this as TextValue).Value;
    }

    public DataHeading AsHeading() {
      return (this as HeadingValue).Value;
    }
    public DataRow AsRow() {
      return (this as TupleValue).Value;
    }
    public DataTable AsTable() {
      return (this as RelationValue).Value;
    }

    public TypedValue[] AsUser() {
      return (this as UserValue).Value;
    }

    public static TypedValue Parse(DataType type, string value) {
      if (type == DataTypes.Bool) return Create(bool.Parse(value));
      if (type == DataTypes.Number) return Create(Decimal.Parse(value));
      if (type == DataTypes.Time) return Create(DateTime.Parse(value));
      if (type == DataTypes.Text) return Create(value);
      return TypedValue.Empty;
    }

    public static BinaryValue Create(byte[] value) {
      return new BinaryValue { Value = value };
    }
    public static PointerValue Create(object value) {
      return new PointerValue { Value = value };
    }
    public static BoolValue Create(bool value) {
      return new BoolValue { Value = value };
    }
    public static CodeValue Create(ExpressionBlock value) {
      return new CodeValue { Value = value };
    }
    public static NumberValue Create(decimal value) {
      return new NumberValue { Value = value };
    }
    public static TimeValue Create(DateTime value) {
      return new TimeValue { Value = value };
    }
    public static TextValue Create(string value) {
      return new TextValue { Value = value };
    }
    public static HeadingValue Create(DataHeading value) {
      return new HeadingValue { Value = value };
    }
    public static TupleValue Create(DataRow value) {
      return new TupleValue { Value = value };
    }
    public static RelationValue Create(DataTable value) {
      return new RelationValue { Value = value };
    }

  }

  ///-------------------------------------------------------------------
  /// <summary>
  /// Base class for comparable typed values
  /// </summary>
  public abstract class ComparableValue : TypedValue, IOrderedValue { // TODO: use this

    public abstract bool IsLess(object other);

    public int CompareTo(object other) {
      return IsLess(other) ? -1
        : Equals(other) ? 0
        : 1;
    }

  }

  ///-------------------------------------------------------------------
  /// <summary>
  /// Void value exists but must not be used in any way
  /// </summary>
  public class VoidValue : TypedValue {
    public static readonly VoidValue Void;
    public static VoidValue Default { get { return Void; } }

    public override string Format() {
      throw new NotImplementedException();
    }
    static VoidValue() {
      Void = new VoidValue { };
    }
    public override DataType DataType {
      get { return DataTypes.Void; }
    }
  }

  ///-------------------------------------------------------------------
  /// <summary>
  /// Boolean value provides True and False values.
  /// </summary>
  public sealed class BoolValue : TypedValue {
    public static readonly BoolValue Default;
    public static readonly BoolValue True;
    public static readonly BoolValue False;
    public bool Value { get; set; }

    static BoolValue() {
      True = new BoolValue { Value = true };
      False = new BoolValue { Value = false };
      Default = False;
    }

    public override string ToString() {
      return Value ? "true" : "false";
    }
    public override string Format() {
      return Value.ToString();
    }
    public override DataType DataType {
      get { return DataTypes.Bool; }
    }
    public override bool Equals(object other) {
      return ((BoolValue)other).Value == Value;
    }
    public override int GetHashCode() {
      return Value ? 1 : 0;
    }
  }

  ///-------------------------------------------------------------------
  /// <summary>
  /// A value that represents an arbitrary value as a sequence of 0 or more bytes.
  /// </summary>
  public class BinaryValue : TypedValue {
    public static BinaryValue Default;
    public byte[] Value { get; set; }

    static BinaryValue() {
      Default = new BinaryValue { Value = new byte[0] };
    }

    public override string ToString() {
      var bs = Value as byte[];
      if (bs == null) return Value.ToString();
      var s = bs.Select(b => String.Format("{0:x2}", b));
      return String.Join("", s);
    }
    public override string Format() {
      return "b'" + ToString() + "'";
    }
    public override DataType DataType {
      get { return DataTypes.Binary; }
    }
    public override bool Equals(object other) {
      var o = other as BinaryValue;
      return o != null && Enumerable.Range(0, Value.Length).All(x => Value[x] == o.Value[x]);
    }
    public override int GetHashCode() {
      return Value.GetHashCode();
    }
  }

  ///-------------------------------------------------------------------
  /// <summary>
  /// A value that represents an arbitrary value as an object.
  /// </summary>
  public class PointerValue : TypedValue {
    public static PointerValue Default;
    public object Value { get; set; }
    
    static PointerValue() {
      Default = new PointerValue { Value = new byte[0] };
    }

    public override string Format() {
      return ToString();
    }
    public override DataType DataType {
      get { return DataTypes.Pointer; }
    }
    public override bool Equals(object other) {
      return other is PointerValue && Value.Equals(((PointerValue)other).Value);
    }
    public override int GetHashCode() {
      return Value.GetHashCode();
    }
  }

  ///-------------------------------------------------------------------
  /// <summary>
  /// A value that represents some kind of number
  /// </summary>
  public class NumberValue : TypedValue, IDataValue, IOrdinalValue {
    public static NumberValue Zero;
    public static NumberValue One;
    public static NumberValue Minimum;
    public static NumberValue Maximum;
    public static NumberValue Default;
    public Decimal Value { get; set; }

    static NumberValue() {
      Zero = new NumberValue { Value = Decimal.Zero };
      One = new NumberValue { Value = Decimal.One };
      Minimum = new NumberValue { Value = Decimal.MinValue };
      Maximum = new NumberValue { Value = Decimal.MaxValue };
      Default = new NumberValue { Value = Decimal.Zero };
    }

    public override string ToString() {
      return Value.ToString();
    }
    public override string Format() {
      return Value.ToString("G");
    }
    public override DataType DataType {
      get { return DataTypes.Number; }
    }
    public override bool Equals(object other) {
      return ((NumberValue)other).Value == Value;
    }
    public override int GetHashCode() {
      return Value.GetHashCode();
    }

    // IOrdinal
    public bool IsLess(object other) {
      return Value < ((NumberValue)other).Value;
    }
    public int CompareTo(object other) {
      return IsLess(other) ? -1
        : Equals(other) ? 0 : 1;
    }
    IOrdinalValue IOrdinalValue.Maximum() {
      return Maximum;
    }
    IOrdinalValue IOrdinalValue.Minimum() {
      return Minimum;
    }
  }

  ///-------------------------------------------------------------------
  /// <summary>
  /// A value that represents some kind of date, time or period
  /// </summary>
  public class TimeValue : TypedValue, IDataValue, IOrdinalValue {
    public static TimeValue Zero;
    public static TimeValue Minimum;
    public static TimeValue Maximum;
    public static TimeValue Default;
    public DateTime Value { get; set; }

    static TimeValue() {
      Zero = new TimeValue { Value = new DateTime(0) };
      Minimum = new TimeValue { Value = DateTime.MinValue };
      Maximum = new TimeValue { Value = DateTime.MaxValue };
      Default = new TimeValue { Value = DateTime.MinValue };
    }

    public override string ToString() {
      return Format();
    }
    public override string Format() {
      if (Value.TimeOfDay.Ticks == 0)
        return Value.ToString("d");
      if (Value.Date == Zero.Value)
        return Value.ToString("t");
      return Value.ToString("g");
    }

    public override DataType DataType {
      get { return DataTypes.Time; }
    }
    public override bool Equals(object other) {
      return ((TimeValue)other).Value == Value;
    }
    public override int GetHashCode() {
      return Value.GetHashCode();
    }

    // IOrdinal
    public bool IsLess(object other) {
      return Value < ((TimeValue)other).Value;
    }
    public int CompareTo(object other) {
      return IsLess(other) ? -1
        : Equals(other) ? 0 : 1;
    }
    IOrdinalValue IOrdinalValue.Maximum() {
      return Maximum;
    }
    IOrdinalValue IOrdinalValue.Minimum() {
      return Minimum;
    }
  }

  ///-------------------------------------------------------------------
  /// <summary>
  /// A value that represents a sequence of 0 or more Unicode characters.
  /// Control chars (C0,C1) tolerated but have no special meanings
  /// </summary>
  public class TextValue : TypedValue, IDataValue, IOrderedValue {
    public static TextValue Default;
    public string Value { get; set; }

    static TextValue() {
      Default = new TextValue { Value = "" };
    }

    public override string ToString() {
      return Value;
    }
    public override string Format() {
      return "'" + Value + "'";
    }
    public override DataType DataType {
      get { return DataTypes.Text; }
    }
    public override bool Equals(object other) {
      return ((TextValue)other).Value == Value;
    }
    public override int GetHashCode() {
      return Value.GetHashCode();
    }

    // IOrdinal
    // Compare strings using current culture. May be not what you expected.
    public bool IsLess(object other) {
      return String.Compare(Value, ((TextValue)other).Value, StringComparison.CurrentCulture) < 0;
    }

    public int CompareTo(object other) {
      return IsLess(other) ? -1
        : Equals(other) ? 0 : 1;
    }
  }

  ///-------------------------------------------------------------------
  /// <summary>
  /// A value that represents a data tuple
  /// </summary>
  public class TupleValue : TypedValue, IDataValue {
    public static TupleValue Default;
    public DataRow Value { get; set; }

    static TupleValue() {
      Default = new TupleValue { Value = DataRow.Empty };
    }

    // delegate formatting to DataRow
    public override string Format() {
      return Value.Format();
    }
    public override string ToString() {
      return Value.ToString();
    }
    public override DataType DataType {
      get { return Value.DataType; }
    }
    public override DataHeading Heading {
      get { return Value.Heading; }
    }
    public override bool Equals(object other) {
      return ((TupleValue)other).Value.Equals(Value);
    }
    public override int GetHashCode() {
      return Value.GetHashCode();
    }
  }

  ///-------------------------------------------------------------------
  /// <summary>
  /// A value that represents a data relation
  /// </summary>
  public class RelationValue : TypedValue, IDataValue {
    public static RelationValue Default;
    public DataTable Value { get; set; }

    static RelationValue() {
      Default = new RelationValue { Value = DataTable.Empty };
    }

    // delegate formatting to DataTable
    public override string ToString() {
      return Value.ToString();
    }
    public override string Format() {
      return Value.Format();
    }
    public override DataType DataType {
      get { return Value.DataType; }
    }
    public override DataHeading Heading {
      get { return Value.Heading; }
    }
    public override bool Equals(object other) {
      var rvother = other as RelationValue;
      return rvother != null && Value.Equals(rvother.Value);
    }
    public override int GetHashCode() {
      return Value.GetHashCode();
    }

  }

  ///-------------------------------------------------------------------
  /// <summary>
  /// A value that represents a heading
  /// </summary>
  public class HeadingValue : TypedValue, IDataValue {
    public static HeadingValue Default;
    public DataHeading Value { get; set; }

    static HeadingValue() {
      Default = new HeadingValue { Value = DataHeading.Empty };
    }

    // delegate formatting to DataHeading
    public override string ToString() {
      return Value.ToString();
    }
    public override string Format() {
      return Value.Format();
    }
    public override DataType DataType {
      get { return DataTypes.Heading; }
    }
    public override DataHeading Heading {
      get { return Value; }
    }
    public override bool Equals(object other) {
      return ((HeadingValue)other).Value.Equals(Value);
    }
    public override int GetHashCode() {
      return Value.GetHashCode();
    }
  }

  ///-------------------------------------------------------------------
  /// <summary>
  /// A value that represents an expression that can be evaluated
  /// </summary>
  public class CodeValue : TypedValue, IDataValue {
    public static CodeValue Default;
    public ExpressionEval AsEval { get { return Value as ExpressionEval; } }
    public ExpressionBlock Value { get; set; }

    static CodeValue() {
      Default = new CodeValue { Value = ExpressionBlock.Empty };
    }

    public override string ToString() {
      return Value.ToString();
    }
    public override string Format() {
      return Value.ToFormat();
    }
    public override DataType DataType {
      get { return DataTypes.Code; }
    }
    public override bool Equals(object other) {
      return ((CodeValue)other).Value.Equals(Value);
    }
    public override int GetHashCode() {
      return Value.GetHashCode();
    }
  }

  ///-------------------------------------------------------------------
  /// <summary>
  /// A value that represents a user-defined data type
  /// 
  /// Stored as an ordered array of other values. Names only exist at compile time.
  /// </summary>
  public class UserValue : TypedValue, IDataValue, IOrderedValue {
    // the default value for the type
    public static UserValue Default;
    // values of each component
    public TypedValue[] Value { get; set; }
    // Hash code calculated from values
    protected int _hashcode;
    // the data type for this value
    public override DataType DataType { get { return _datatype; } }
    protected DataTypeUser _datatype;

    static UserValue() {
      Default = Create(new TypedValue[0], DataTypeUser.Empty);
    }

    static public UserValue Create(TypedValue[] value, DataTypeUser datatype) {
      if (datatype.Name == "date") return Builtin.DateValue.Create(value[0] as TimeValue);
      var ret = new UserValue { Value = value, _datatype = datatype };
      ret._hashcode = ret.CalcHashCode();
      return ret;
    }

    public override string ToString() {
      // special case, please avoid
      if (Value == null) return "()";
      var str = String.Join(",", Value.Select(s => s.ToString()));
      return str;
    }
    public override string Format() {
      if (Value == null) return "()";
      var str = _datatype.Name + "(" + String.Join(",", Value.Select(s => s.Format())) + ")";
      return str;
    }

    public override bool Equals(object other) {
      var uvo = (UserValue)other;
      if (uvo == null || uvo.DataType != DataType || uvo.GetHashCode() != GetHashCode()) return false;
      return Enumerable.Range(0, Value.Length)
        .All(x => Value[x].Equals(uvo.Value[x]));
    }

    public override int GetHashCode() {
      Logger.Assert(_hashcode == CalcHashCode(), "uservalue hashcode");
      return _hashcode;
    }

    // IOrdinal
    public bool IsLess(object other) {
      var uvo = (UserValue)other;
      return DataType == uvo.DataType &&
        Enumerable.Range(0, Value.Length)
        .All(x => ((IOrdinalValue)Value[x]).IsLess(uvo.Value[x]));
    }

    public int CompareTo(object other) {
      return IsLess(other) ? -1
        : Equals(other) ? 0 : 1;
    }

    public TypedValue GetComponentValue(string name) {
      var index = _datatype.Heading.FindIndex(name);
      return index == -1 ? TypedValue.Empty : Value[index];
    }

    // internal calculate hash code
    // independent of column order, but need not be so
    protected int CalcHashCode() {
      var hash = 0;
      for (var i = 0; i < Value.Length; ++i) {
        var code = Value[i].GetHashCode();
        hash ^= code;
      }
      return hash;
    }

  }

  ///-------------------------------------------------------------------
  /// <summary>
  /// A value of a type that is a subtype
  /// 
  /// Basically just one level of indirection plus a predicate constraint (TBD)
  /// </summary>
  //public class SubtypeValue : TypedValue, IDataValue {
  //  // the default value for the type
  //  public static SubtypeValue Default;

  //  public DataTypeSubtype _datatype;
  //  public TypedValue Value { get; private set; }

  //  static SubtypeValue() {
  //    Default = Create(TypedValue.Empty, DataTypes.Subtype as DataTypeSubtype);
  //  }

  //  static public SubtypeValue Create(TypedValue value, DataTypeSubtype datatype) {
  //    return new SubtypeValue {
  //      Value = value,
  //      _datatype = datatype,
  //    };
  //  }

  //  // Recursively find base value
  //  TypedValue BaseValue() {
  //    if (Value is SubtypeValue)
  //      return (Value as SubtypeValue).BaseValue();
  //    else return Value;
  //  }

  //  // delegate formatting
  //  public override string ToString() {
  //    return BaseValue().ToString();
  //  }
  //  public override string Format() {
  //    return BaseValue().Format();
  //  }
  //  public override DataType DataType {
  //    get { return _datatype; }
  //  }
  //  public override bool Equals(object other) {
  //    var rvother = other as SubtypeValue;
  //    return rvother != null && Value.Equals(rvother.Value);
  //  }
  //  public override int GetHashCode() {
  //    return Value.GetHashCode();
  //  }

  //}
}

