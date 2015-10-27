using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;

namespace Andl.Runtime {
  public class TypeMaker {
    /// <summary>
    /// Convert a single TypedValue to or from its corresponding native value.
    /// 
    /// Primitive values are converted directly to system types. 
    /// Structured values depending on first creating a type, then filling an instance.
    /// Tuple or user is converted to a simple class instance
    /// Relation is convered to a generic list of class instances
    /// 
    /// All native values are cast to or from object.
    /// </summary>
    static AssemblyName _assemblyname = new AssemblyName("TypeMakerAssembly");
    static AssemblyBuilder _assbuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(_assemblyname, AssemblyBuilderAccess.RunAndCollect);
    static ModuleBuilder _modulebuilder = _assbuilder.DefineDynamicModule("TypeMakerModule");

    // type builder for creating structured types
    TypeBuilder _typebuilder;

    // Use this type maker to create a structured type, recursively
    public static Type CreateType(DataType datatype) {
      // For relation wrap tuple type in a generic List<>
      if (datatype is DataTypeRelation) {
        var listtype = typeof(List<>);
        return listtype.MakeGenericType((datatype as DataTypeRelation).ChildTupleType.NativeType);
      }
      var typemaker = new TypeMaker {
        _typebuilder = _modulebuilder.DefineType(datatype.GetNiceName, TypeAttributes.Public),
      };
      typemaker.DefineMembers(datatype.Heading.Columns);
      return typemaker._typebuilder.CreateType();
    }

    void DefineMembers(DataColumn[] columns) {
      foreach (var col in columns)
        if (col.Name != "")   // special for anonymous in Lift
          DefineField(col.Name, col.DataType);
    }

    void DefineField(string name, DataType datatype) {
      Logger.Assert(datatype.NativeType != null, datatype);
      _typebuilder.DefineField(name, datatype.NativeType, FieldAttributes.Public);
    }

    void DefineField(string name, Type type) {
      _typebuilder.DefineField(name, type, FieldAttributes.Public);
    }

    // any -- dispatch
    public static object ToNativeValue(TypedValue value) {
      if (!_fillerdict.ContainsKey(value.DataType.BaseName)) return null;
      return _fillerdict[value.DataType.BaseName](value);
    }

    // any -- dispatch
    public static TypedValue FromNativeValue(object value, DataType datatype) {
      if (!_getterdict.ContainsKey(datatype.BaseName)) return null;
      return _getterdict[datatype.BaseName](value, datatype);
    }

    // --- impl

    static Dictionary<string, Func<TypedValue, object>> _fillerdict = new Dictionary<string, Func<TypedValue, object>> {
      { "binary",   (v) => ((BinaryValue)v).Value },
      { "bool",     (v) => ((BoolValue)v).Value },
      { "number",   (v) => ((NumberValue)v).Value },
      { "tuple",    (v) => GetNativeValue(v.DataType, ((TupleValue)v).Value) },
      { "relation", (v) => GetNativeValue(v.DataType as DataTypeRelation, ((RelationValue)v).Value.GetRows()) },
      { "text",     (v) => ((TextValue)v).Value },
      { "time",     (v) => ((TimeValue)v).Value },
      { "date",     (v) => ((TimeValue)v).Value },  // FIX: temp
      { "user",     (v) => GetNativeValue(v.DataType, ((UserValue)v).Value) },
    };

    static Dictionary<string, Func<object, DataType, TypedValue>> _getterdict = new Dictionary<string, Func<object, DataType, TypedValue>> {
      { "binary",   (v, dt) => BinaryValue.Create(v as byte[]) },
      { "bool",     (v, dt) => BoolValue.Create((bool)v) },
      { "number",   (v, dt) => NumberValue.Create((decimal)v) },
      { "tuple",    (v, dt) => TupleValue.Create(DataRow.Create(dt.Heading, GetValues(v, dt.Heading.Columns))) },
      { "relation", (v, dt) => RelationValue.Create(DataTableLocal.Create(dt.Heading, GetRows(v, dt.Heading.Columns))) },
      { "text",     (v, dt) => TextValue.Create(v as string) },
      { "time",     (v, dt) => TimeValue.Create((DateTime)v) },
      { "date",     (v, dt) => Builtin.DateValue.Create((DateTime)v) },   // FIX: temp
      { "user",     (v, dt) => UserValue.Create(GetValues(v, dt.Heading.Columns), dt as DataTypeUser) },    };

    // tuple -- with ordered heading
    static object GetNativeValue(DataType datatype, DataRow row) {
      return FillInstance(datatype.NativeType, row.Heading.Columns, row.Values);
    }

    // user type
    static object GetNativeValue(DataType datatype, TypedValue[] values) {
      return FillInstance(datatype.NativeType, datatype.Heading.Columns, values);
    }

    // create and fill an instance give its rows, columns and values
    // TODO: optimise?
    static object GetNativeValue(DataTypeRelation datatype, IEnumerable<DataRow> rows) {
      var instance = Activator.CreateInstance(datatype.NativeType);
      var addmethod = datatype.NativeType.GetMethod("Add");
      var rowtype = addmethod.GetParameters()[0].ParameterType;
      foreach (var row in rows) {
        // use heading from row to ensure correct field order
        var rowinstance = FillInstance(rowtype, row.Heading.Columns, row.Values);
        addmethod.Invoke(instance, new object[] { rowinstance });
      }
      return instance;
    }

    // create and fill an instance give its columns and values
    // TODO: heavily optimise this code
    static object FillInstance(Type type, DataColumn[] columns, TypedValue[] values) {
      var instance = Activator.CreateInstance(type);
      for (var colx = 0; colx < columns.Length; ++colx) {
        var col = columns[colx];
        var fieldinfo = type.GetField(col.Name);
        fieldinfo.SetValue(instance, ToNativeValue(values[colx]));
      }
      return instance;
    }

    // extract values from an instance
    static TypedValue[] GetValues(object instance, DataColumn[] columns) {
      var values = new TypedValue[columns.Length];
      var type = instance.GetType();
      for (var colx = 0; colx < columns.Length; ++colx) {
        var col = columns[colx];
        var fieldinfo = type.GetField(col.Name);
        var value = fieldinfo.GetValue(instance);
        values[colx] = FromNativeValue(value, col.DataType);
      }
      return values;
    }

    static IEnumerable<DataRow> GetRows(object instance, DataColumn[] columns) {
      var type = instance.GetType();
      var countpi = type.GetProperty("Count");
      var itempi = type.GetProperty("Item");
      var count = (int)countpi.GetValue(instance, null);

      var rows = new List<DataRow>();
      var heading = DataHeading.Create(columns, false); // preserve column order
      for (var x = 0; x < count; ++x) {
        var row = itempi.GetValue(instance, new object[] { x });
        var values = GetValues(row, columns);
        rows.Add(DataRow.Create(heading, values));
      }
      return rows;
    }
  }

}
