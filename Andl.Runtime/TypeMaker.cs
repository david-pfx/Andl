using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;

namespace Andl.Runtime {
  public class TypeMaker {
    static AssemblyName _assemblyname = new AssemblyName("TypeMakerAssembly");
    static AssemblyBuilder _assbuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(_assemblyname, AssemblyBuilderAccess.RunAndCollect);
    static ModuleBuilder _modulebuilder = _assbuilder.DefineDynamicModule("TypeMakerModule");

    TypeBuilder _typebuilder;

    // Use this type maker to create a type, recursively
    public static Type CreateType(DataType datatype) {
      var typemaker = new TypeMaker {
        _typebuilder = _modulebuilder.DefineType(datatype.GenCleanName, TypeAttributes.Public),
      };
      typemaker.DefineMembers(datatype.Heading.Columns);
      var type = typemaker._typebuilder.CreateType();
      // Wrap a relation in a generic List<>
      if (datatype is DataTypeRelation) {
        var listtype = typeof(List<>);
        return listtype.MakeGenericType(type);
      } else return type;
    }

    public void DefineMembers(DataColumn[] columns) {
      foreach (var col in columns)
        if (col.Name != "")   // special for anonymous in Lift
          DefineField(col.Name, col.DataType);
    }

    public void DefineField(string name, DataType datatype) {
      Logger.Assert(datatype.NativeType != null, datatype);
      _typebuilder.DefineField(name, datatype.NativeType, FieldAttributes.Public);
    }

    void DefineField(string name, Type type) {
      _typebuilder.DefineField(name, type, FieldAttributes.Public);
    }

    public static Dictionary<DataType, Func<TypedValue, object>> _fillerdict = new Dictionary<DataType, Func<TypedValue, object>> {
      { DataTypes.Binary, (v) => ((BinaryValue)v).Value },
      { DataTypes.Bool,   (v) => ((BoolValue)v).Value },
      { DataTypes.Number, (v) => ((NumberValue)v).Value },
      { DataTypes.Row,    (v) => GetNativeValue(v.DataType, ((TupleValue)v).Value.Values) },
      { DataTypes.Table,  (v) => GetNativeValue(v.DataType as DataTypeRelation, ((RelationValue)v).Value.GetRows()) },
      { DataTypes.Text,   (v) => ((TextValue)v).Value },
      { DataTypes.Time,   (v) => ((TimeValue)v).Value },
      { DataTypes.User,   (v) => GetNativeValue(v.DataType, ((UserValue)v).Value) },
    };

    public static object GetNativeValue(TypedValue value) {
      if (!_fillerdict.ContainsKey(value.DataType.BaseType)) return null;
      //Logger.Assert(_fillerdict.ContainsKey(value.DataType.BaseType), value.DataType.BaseName);
      return _fillerdict[value.DataType.BaseType](value);
    }

    public static object GetNativeValue(DataType datatype, TypedValue[] values) {
      return FillInstance(datatype.NativeType, datatype.Heading.Columns, values);
    }

    // create and fill an instance give its rows, columns and values
    // TODO: optimise?
    public static object GetNativeValue(DataTypeRelation datatype, IEnumerable<DataRow> rows) {
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
        fieldinfo.SetValue(instance, GetNativeValue(values[colx]));
      }
      return instance;
    }

  }

}
