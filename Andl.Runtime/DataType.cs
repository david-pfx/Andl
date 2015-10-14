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
  [Flags]
  public enum TypeFlags {
    None = 0, Ordered = 1, Ordinal = 4, Variable = 8, Generated = 16, HasHeading = 32, HasName = 64
  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  ///  Interface to be implemented by all types
  /// </summary>
  public interface IDataType {
    // Name of this type
    string Name { get; }
    // parse a string to return a value of this type
    TypedValue CreateValue(object value);
    // provide a default value for the type
    TypedValue DefaultValue();
    // return a heading if available OBS:?
    DataHeading Heading { get; }
    // Return the type flags
    TypeFlags Flags { get; }
    // return true if this type is a subtype of the other
    bool IsSubtype(IDataType other);
  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Holder for pre-defined types
  /// 
  /// These are here so the compiler can access them statically.
  /// </summary>
  public class DataTypes {
    // fake types, compiler only
    public static readonly DataType Unknown;
    public static readonly DataType Any;
    public static readonly DataType Default;  // default for import
    // runtime argument types, not user visible
    public static readonly DataType Code;
    public static readonly DataType CodeArray;
    public static readonly DataType Heading;
    public static readonly DataType Ordered;
    public static readonly DataType Ordinal;
    public static readonly DataType Pointer;
    public static readonly DataType ValueArray;
    public static readonly DataType Void;
    // user visible, scalars and non-scalars
    public static readonly DataType Binary;
    public static readonly DataType Bool;
    public static readonly DataType Number;
    public static readonly DataType Time;
    public static readonly DataType Text;
    // base type for generated types
    public static readonly DataType Table;
    public static readonly DataType Row;
    public static readonly DataType User;

    public static Dictionary<string, DataType> Dict { get; private set; }
    public static Dictionary<Type, DataType> TypeDict { get; private set; }

    /// <summary>
    /// Create a type object for each type. Types can simply be compared for equality.
    /// </summary>
    static DataTypes() {
      Dict = new Dictionary<string, DataType>();
      TypeDict = new Dictionary<Type, DataType>();

      Unknown = DataType.Create("unknown", null);
      Any = DataType.Create("any", typeof(TypedValue));
      Ordered = DataType.Create("ordered", typeof(IOrderedValue));
      Ordinal = DataType.Create("ordinal", typeof(IOrdinalValue));
      CodeArray = DataType.Create("code[]", typeof(CodeValue[]));
      ValueArray = DataType.Create("value[]", typeof(TypedValue[]));
      Void = DataType.Create("void", typeof(VoidValue), null,
        null, () => VoidValue.Default);
      Code = DataType.Create("code", typeof(CodeValue), null, 
        v => CodeValue.Create((ExpressionBlock)v), () => CodeValue.Default);
      Bool = DataType.Create("bool", typeof(BoolValue), typeof(bool),
        v => BoolValue.Create((bool)v), () => BoolValue.Default, TypeFlags.Variable);
      Binary = DataType.Create("binary", typeof(BinaryValue), typeof(byte[]),
        v => null, () => BinaryValue.Default, TypeFlags.Variable);
      Pointer = DataType.Create("reference", typeof(PointerValue), typeof(IntPtr),
        v => null, () => PointerValue.Default, TypeFlags.Variable);
      Number = DataType.Create("number", typeof(NumberValue), typeof(decimal),
        v => NumberValue.Create((decimal)v), () => NumberValue.Default, TypeFlags.Ordered | TypeFlags.Ordinal | TypeFlags.Variable);
      Time = DataType.Create("time", typeof(TimeValue), typeof(DateTime),
        v => TimeValue.Create((DateTime)v), () => TimeValue.Default, TypeFlags.Ordered | TypeFlags.Ordinal | TypeFlags.Variable);
      Text = DataType.Create("text", typeof(TextValue), typeof(string),
        v => TextValue.Create((string)v), () => TextValue.Default, TypeFlags.Ordered | TypeFlags.Variable);
      Heading = DataType.Create("heading", typeof(HeadingValue), null,
        v => HeadingValue.Create((DataHeading)v), () => HeadingValue.Default, TypeFlags.HasHeading);
      // specials -- actually subtypes, one created here, more by user code
      // defaults will be overwritten for generated types
      Row = DataType.Create("tuple", typeof(TupleValue), null,
        v => TupleValue.Create((DataRow)v), () => TupleValue.Default, TypeFlags.HasHeading);
      Table = DataType.Create("relation", typeof(RelationValue), null,
        v => RelationValue.Create((DataTable)v), () => RelationValue.Default, TypeFlags.HasHeading);
      User = DataType.Create("user", typeof(UserValue), null,
        v => UserValue.Create((TypedValue)v), () => UserValue.Default, TypeFlags.HasHeading | TypeFlags.HasName);
      Default = Text;
    }

    // dummy to force class construction
    public static void Init() { }

    // Find type in dictionary
    public static DataType Find(string name) {
      return name != null && Dict.ContainsKey(name) ? Dict[name] : null;
    }
  }

  public delegate TypedValue ConvertDelegate(object value);
  public delegate TypedValue DefaultDelegate();
  public delegate bool IsSubclassDelegate(IDataType other);

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Implements objects that define the class of a TypedValue
  /// 
  /// One created for each data type
  /// </summary>
  public class DataType : IDataType {
    public const string TupleNiceNameTemplate = "__t_{0}";
    public const string TupleUniqueNameTemplate = "{{{0}}}";
    public const string RelationUniqueNameTemplate = "{{{{{0}}}}}";

    public string Name { get; protected set; }
    public TypeFlags Flags { get; protected set; }
    // return heading if it has one
    public virtual DataHeading Heading { get; protected set; }
    // Return default value for the type. Overridden in generated types
    public virtual TypedValue DefaultValue() {
      return _defaulter();
    }

    // type object that can hold a value of this type directly, for serialisation
    public Type NativeType { get; protected set; }

    // private
    protected ConvertDelegate _converter;
    protected DefaultDelegate _defaulter;
    protected IsSubclassDelegate _issubclass;

    public bool IsOrdered { get { return Flags.HasFlag(TypeFlags.Ordered); } }
    public bool IsOrdinal { get { return Flags.HasFlag(TypeFlags.Ordinal); } }
    public bool IsVariable { get { return Flags.HasFlag(TypeFlags.Variable); } }
    public bool IsGenerated { get { return Flags.HasFlag(TypeFlags.Generated); } }
    public bool HasHeading { get { return Flags.HasFlag(TypeFlags.HasHeading); } }
    public bool HasName { get { return Flags.HasFlag(TypeFlags.HasName); } }

    public override bool Equals(object obj) {
      return obj is DataType && (obj as DataType).Name == Name;
    }

    public override int GetHashCode() {
      return Name.GetHashCode();
    }

    public override string ToString() {
      return Name;
    }

    // Create minimal type and add to dictionary
    public static DataType Create(string name, Type valuetype) {
      return Create(name, valuetype, null, null, null);
    }

    // Create type and add to dictionary
    public static DataType Create(string name, Type valuetype, Type nativetype, ConvertDelegate converter, DefaultDelegate defaulter, TypeFlags flags = TypeFlags.None) {
      return Create(name, valuetype, nativetype, converter, defaulter, x => false, flags);
    }

    // Create type and add to dictionary
    public static DataType Create(string name, Type valuetype, Type nativetype, ConvertDelegate converter, DefaultDelegate defaulter, IsSubclassDelegate issubclass, TypeFlags flags = TypeFlags.None) {
      var dt = new DataType {
        Name = name,
        _converter = converter,
        _defaulter = defaulter,
        _issubclass = issubclass,
        NativeType = nativetype,
        Flags = flags,
      };
      DataTypes.Dict[name] = dt;
      if (valuetype != null) DataTypes.TypeDict[valuetype] = dt;
      return dt;
    }

    public TypedValue CreateValue(object value) {
      return _converter(value);
    }

    // Override this and return true as needed
    public bool IsSubtype(IDataType other) {
      return false; // FIX: _issubclass(other);
    }

    // Base type sets common behaviour for the type
    public virtual DataType BaseType { get { return this; } }
    // Base name from base type
    public string BaseName { get { return BaseType.Name; } }
    // Name guaranteed to be unique for generated types and null for others
    public virtual string GetUniqueName { get { return null; } }
    // Name for code generation, unique (enough) on its own, never null
    public virtual string GetNiceName { get { return BaseName; } }

    // Is other a type match where this is what is needed?
    public bool IsTypeMatch(DataType other) {
      var ok = this == other  
        || this == DataTypes.Any  // runtime to handle
        //|| this == DataTypes.Text // coercion -- too hard for now
        || other.IsSubtype(this)
        || this == DataTypes.Table && other is DataTypeRelation
        || this == DataTypes.Row && other is DataTypeTuple
        || this == DataTypes.Ordered && other.IsOrdered
        || this == DataTypes.Ordinal && other.IsOrdinal;
      if (!ok) Logger.WriteLine(5, "Type mismatch this:{0} other:{1}", this, other);
      return ok;
    }

    // construct a type from a base name, optional heading and optional user type name
    public static DataType Derive(DataType basetype, DataHeading heading, string username) {
      if (basetype == DataTypes.Table)
        return DataTypeRelation.Get(heading);
      if (basetype == DataTypes.Row)
        return DataTypeTuple.Get(heading);
      if (basetype == DataTypes.User)
        return DataTypeUser.Get(username, heading.Columns);
      return basetype;
    }

    public static TypedValue[] MakeDefaultValues(DataHeading heading) {
      var values = new TypedValue[heading.Degree];
      for (var x = 0; x < values.Length; ++x)
        values[x] = heading.Columns[x].DataType.DefaultValue();
      return values;
    }

  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Tuple subtype includes a Heading
  /// </summary>
  public class DataTypeTuple : DataType {
    public override DataHeading Heading { get; protected set; }
    public int Ordinal { get; private set; }
    // A nice name that can be used for code generated from this type
    public string NiceName { get; set; }

    static Dictionary<DataHeading, DataTypeTuple> _headings = new Dictionary<DataHeading, DataTypeTuple>();
    TupleValue _default;

    public override bool Equals(object obj) {
      return obj is DataTypeTuple && (obj as DataTypeTuple).Heading.Equals(Heading);
    }

    public override int GetHashCode() {
      return Heading.GetHashCode();
    }

    public override string ToString() {
      return base.ToString() + ":" + Heading.ToString();
    }

    public override DataType BaseType { get { return DataTypes.Row; } }

    public override string GetUniqueName { get { return string.Format(TupleUniqueNameTemplate, Ordinal); } }
    public override string GetNiceName { get { return NiceName ?? string.Format(TupleNiceNameTemplate, Ordinal); } }

    public override TypedValue DefaultValue() {
      if (_default == null)
        _default = TupleValue.Create(DataRow.Create(Heading, MakeDefaultValues(Heading)));
      return _default;
    }

    // Create a new relation type for a particular heading
    // Called once for the generic, then once for each specific
    static DataTypeTuple Create(string name, DataHeading heading, TypeFlags flags, ConvertDelegate converter = null, DefaultDelegate defaulter = null) {
      var dt = new DataTypeTuple {
        Name = name,
        Heading = heading,
        _converter = converter ?? (v => TupleValue.Create((DataTable)v)),
        //_defaulter = defaulter ?? (() => TupleValue.Default), //BUG:xxx
        Flags = flags,
      };
      return dt;
    }

    // Get type from dictionary, or create and add
    public static DataTypeTuple Get(DataHeading heading) {
      if (_headings.ContainsKey(heading)) return _headings[heading];
      var dt = DataTypeTuple.Create("tuple", heading, TypeFlags.Variable | TypeFlags.Generated | TypeFlags.HasHeading);
      dt.Ordinal = _headings.Count + 1;
      dt.NativeType = TypeMaker.CreateType(dt);
      _headings[heading] = dt;
      return dt;
    }

    public RelationValue CreateValue(List<TypedValue[]> values) {
      var rows = values.Select(v => DataRow.Create(Heading, v));
      return RelationValue.Create(DataTableLocal.Create(Heading, rows));
    }

    // Give this type a possible clean name if it doesn't already have one
    public void ProposeCleanName(string name) {
      if (NiceName == null)
        NiceName = name;
    }
  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Relation Subtype includes a Heading
  /// </summary>
  public class DataTypeRelation : DataType {
    public static DataTypeRelation Empty { get { return Get(DataHeading.Empty);  } }
    public int Ordinal { get; private set; }
    public override DataHeading Heading { get; protected set; }
    // the linked tuple type
    public DataTypeTuple ChildTupleType { get; private set; }

    RelationValue _default;

    static Dictionary<DataHeading, DataTypeRelation> _headings = new Dictionary<DataHeading, DataTypeRelation>();

    public override bool Equals(object obj) {
      return obj is DataTypeRelation && (obj as DataTypeRelation).Heading.Equals(Heading);
    }

    public override int GetHashCode() {
      return Heading.GetHashCode();
    }

    public override string ToString() {
      return base.ToString() + ":" + Heading.ToString();
    }

    public override DataType BaseType { get { return DataTypes.Table; } }

    public override string GetUniqueName { get { return string.Format(RelationUniqueNameTemplate, Ordinal); } }
    public override string GetNiceName { get { return ChildTupleType.GetNiceName; } }

    public override TypedValue DefaultValue() {
      if (_default == null)
        _default = RelationValue.Create(DataTable.Create(Heading));
      return _default;
    }

    // Create a new relation type for a particular heading
    // Called once for the generic, then once for each specific
    public static DataTypeRelation Create(string name, DataHeading heading, TypeFlags flags, ConvertDelegate converter = null, DefaultDelegate defaulter = null) {
      var dt = new DataTypeRelation {
        Name = name,
        Heading = heading,
        _converter = converter ?? (v => RelationValue.Create((DataTable)v)),
        //_defaulter = defaulter ?? (() => RelationValue.Default), //BUG:xxx
        Flags = flags,
      };
      return dt;
    }

    // Get type from dictionary, or create and add
    // Every relation needs a row type, so make sure they use the same heading
    public static DataTypeRelation Get(DataHeading heading) {
      if (_headings.ContainsKey(heading)) return _headings[heading];
      var tupletype = DataTypeTuple.Get(heading);
      var dt = DataTypeRelation.Create("relation", tupletype.Heading, TypeFlags.Variable | TypeFlags.Generated | TypeFlags.HasHeading);
      dt.Ordinal = _headings.Count + 1;
      dt.ChildTupleType = tupletype;
      dt.NativeType = TypeMaker.CreateType(dt);
      _headings[heading] = dt;
      var x = Activator.CreateInstance(dt.NativeType);
      return dt;
    }

    // Create a value for this type
    public TupleValue CreateValue(TypedValue[] values) {
      return TupleValue.Create(DataRow.Create(Heading, values));
    }

    // Give this type a possible clean name if it doesn't already have one
    public void ProposeCleanName(string name) {
      ChildTupleType.ProposeCleanName(name);
    }
  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// User-defined Subtype includes a Heading
  /// </summary>
  public class DataTypeUser : DataType {
    static Dictionary<string, DataTypeUser> _usertypes = new Dictionary<string, DataTypeUser>();
    UserValue _default;

    public static DataTypeUser Empty { get { return Get(":empty", new DataColumn[0]); } }
    public override DataHeading Heading { get; protected set; }


    public override bool Equals(object obj) {
      var udto = obj as DataTypeUser;
      return udto != null && Name == udto.Name && Heading.Equals(udto.Heading);
    }

    public override int GetHashCode() {
      return Heading.GetHashCode();
    }

    public override string ToString() {
      return Name + ":" + Heading.ToString();
    }

    public override DataType BaseType { get { return DataTypes.User; } }
    public override string GetUniqueName { get { return Name; } }
    public override string GetNiceName { get { return Name; } }

    public override TypedValue DefaultValue() {
      if (_default == null)
        _default = UserValue.Create(MakeDefaultValues(Heading), this);
      return _default;
    }

    // Create a new User type for a particular heading
    public static DataTypeUser Create(string name, DataHeading heading, TypeFlags flags, ConvertDelegate converter = null, DefaultDelegate defaulter = null) {
      var dt = new DataTypeUser {
        Name = name,
        Heading = heading,
        _converter = converter ?? (v => UserValue.Create((TypedValue[])v)),
        //_defaulter = defaulter,
        Flags = flags,
      };
      dt.NativeType = TypeMaker.CreateType(dt);
      return dt;
    }

    //// Return default value for the type. 
    //public UserValue GetDefault() {
    //  if (_default == null) {
    //    var values = new TypedValue[Heading.Degree];
    //    for (var x = 0; x < values.Length; ++x)
    //      values[x] = Heading.Columns[x].DataType.Default();
    //    _default = UserValue.Create(values, this);
    //  }
    //  return _default;
    //}

    // Create and add, return new type (must not exist)
    public static DataTypeUser Get(string name, DataColumn[] columns) {
      var old = Find(name);
      if (old != null && columns.SequenceEqual(old.Heading.Columns)) return old;
      Logger.Assert(!_usertypes.ContainsKey(name), name);
      var flags = columns.Any(c => c.DataType.IsOrdered) ? TypeFlags.Ordered : TypeFlags.None;
      var dt = DataTypeUser.Create(name, DataHeading.Create(columns), flags | TypeFlags.Variable | TypeFlags.Generated | TypeFlags.HasHeading);
      _usertypes[name] = dt;
      return dt;
    }

    // Find type in dictionary
    public static DataTypeUser Find(string name) {
      return name != null && _usertypes.ContainsKey(name) ? _usertypes[name] : null;
    }

    // Create a value for this type
    public UserValue CreateValue(TypedValue[] values) {
      return UserValue.Create(values, this);
    }

  }
}
