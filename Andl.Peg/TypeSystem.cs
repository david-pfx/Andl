using System;
using System.Collections.Generic;
using System.Linq;
using Andl.Runtime;
using Andl.Common;

namespace Andl.Peg {
  //public class Field {
  //  public string Name;
  //  public DataType Type;
  //}

  /// <summary>
  /// Type system for parser: a layer over the Runtime type system
  /// </summary>
  public class TypeSystem {
    public PegParser Parser { get; set; }

    public DataType Find(string name) {
      var dt = DataTypes.Find(name);
      return dt != null && dt.IsVariable ? dt : null;
    }

    internal DataType Find(IEnumerable<DataColumn> columns) {
      return DataTypeRelation.Get(DataHeading.Create(columns));
    }

    internal DataType Tupof(DataType type) {
      return DataTypeTuple.Get(type.Heading);
    }
    internal DataType Tupof(DataHeading heading) {
      return DataTypeTuple.Get(heading);
    }
    internal DataType Relof(DataType type) {
      return DataTypeRelation.Get(type.Heading);
    }
    internal DataType Relof(DataHeading heading) {
      return DataTypeRelation.Get(heading);
    }

    ///============================================================================================
    /// Type checking
    /// 

    // check symbol with a method and arguments, return return type and callinfo
    // raises error if type error detected 
    // no of args: symbol table sets a limit because some builtins have extra trailing args
    public void CheckTypeError(Symbol symbol, out DataType datatype, out CallInfo callinfo, params DataType[] datatypes) {
      datatype = symbol.DataType;
      callinfo = symbol.CallInfo;
      var nargs = symbol.NumArgs; // max no to check
      if (datatypes.Length > nargs)
        Parser.ParseError("'{0}' expected {1} arguments, found {2}", symbol.Name, nargs, datatypes.Length);
      if (symbol.IsCompareOp && datatypes[0] != datatypes[1])
        Parser.ParseError("'{0}' arguments must be same type", symbol.Name);
      var match = false;
      var hasoverloads = symbol.CallInfo.OverLoad != null;
      for (var cinf = symbol.CallInfo; cinf != null && !match; cinf = cinf.OverLoad) {
        var nargsi = Math.Min(symbol.NumArgs, cinf.NumArgs); // max no to check
        if (datatypes.Length == nargsi
          && Enumerable.Range(0, nargsi).All(x => TypeMatch(cinf.Arguments[x].DataType, datatypes[x]))) {  
          if (match)
            Parser.ParseError("'{0}' ambiguous type match", symbol.Name);
          match = true;
          callinfo = cinf;
          if (hasoverloads)   // assume symbol table correct unless using overloads
            datatype = cinf.ReturnType; //FIX: bad feeling about this
          else if (datatype == DataTypes.Ordered)
            datatype = datatypes[0];  // FIX: ouch
        }
      }
      if (!match)
        Parser.ParseError("no definition matches call to '{0}'", symbol.Name);
      if (symbol.IsDyadic)
        CheckDyadicType(symbol, datatypes[0], datatypes[1], ref datatype);
      else if (datatype == DataTypes.Table || datatype == DataTypes.Infer)
        CheckInferType(symbol, datatypes, ref datatype);
      else if (datatype == DataTypes.Unknown)
        Parser.ParseError($"{symbol.Name}: cannot infer return type");
      Logger.Assert(datatype.Flags.HasFlag(TypeFlags.Variable) || datatype == DataTypes.Void, datatype.Name);
    }

    // check dyadic ops and compute result type
    void CheckDyadicType(Symbol sym, DataType datatype1, DataType datatype2, ref DataType datatype) {
      var joinop = sym.JoinOp;
      if (datatype1 is DataTypeRelation && datatype2 is DataTypeRelation) {
        var cols = DataColumn.Merge(Symbol.ToMergeOp(joinop), datatype1.Heading.Columns, datatype2.Heading.Columns);
        var dups = cols.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (dups.Length > 0)
          Parser.ParseError($"{sym.Name}: duplicate attribute: {dups.Join(",")}");
        datatype = DataTypeRelation.Get(DataHeading.Create(cols));
      } else if (datatype1 is DataTypeTuple && datatype2 is DataTypeTuple) {
        var cols = DataColumn.Merge(Symbol.ToTupleOp(joinop), datatype1.Heading.Columns, datatype2.Heading.Columns);
        var dups = cols.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (dups.Length > 0)
          Parser.ParseError($"{sym.Name}: duplicate attribute: {dups.Join(",")}");
        datatype = DataTypeTuple.Get(DataHeading.Create(cols));
      } else
        Parser.ParseError($"{sym.Name}: expected relational or tuple arguments");
    }

    // Functions that return a Table take their type from the first argument
    void CheckInferType(Symbol sym, DataType[] datatypes, ref DataType datatype) {
      if (datatype == DataTypes.Table && datatypes[0] is DataTypeRelation)
        datatype = datatypes[0];
      else if (datatype == DataTypes.Infer)
        datatype = datatypes[0];
      else Parser.ParseError($"{sym.Name}: cannot infer type from argument 1");
    }

    // check single type match
    public void CheckTypeMatch(DataType typeexp, DataType typeis) {
      if (!TypeMatch(typeexp, typeis)) Parser.ParseError("type mismatch, expected '{0}'", typeexp);
    }

    // Is type2 a type match where type1 is what is needed?
    public bool TypeMatch(DataType typeexp, DataType typeis) {
      var ok = typeexp == typeis
        || typeexp == DataTypes.Any  // runtime to handle
        || typeexp == DataTypes.Code  // runtime to handle
        || typeexp == DataTypes.CodeArray  // runtime to handle
        || typeis.IsSubtype(typeexp)
        || typeexp == DataTypes.Table && typeis is DataTypeRelation
        || typeexp == DataTypes.Row && typeis is DataTypeTuple
        || typeexp == DataTypes.User && typeis is DataTypeUser
        || typeexp == DataTypes.Ordered && typeis.IsOrdered
        || typeexp == DataTypes.Ordinal && typeis.IsOrdinal;
      return ok;
    }


  }
}
