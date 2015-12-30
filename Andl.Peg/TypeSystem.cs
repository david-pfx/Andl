using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Andl.Runtime;

namespace Andl.Peg {
  public class Field {
    public string Name;
    public DataType Type;
  }

  /// <summary>
  /// Type system for parser: a layer over the Runtime type system
  /// </summary>
  public class TypeSystem {
    public PegParser Parser { get; set; }

    public DataType Find(string name) {
      var dt = DataTypes.Find(name);
      return dt != null && dt.IsVariable ? dt : null;
    }

    internal DataType Find(IEnumerable<Field> typelist) {
      var heading = DataHeading.Create(typelist.Select(f => DataColumn.Create(f.Name, f.Type)));
      return DataTypeTuple.Get(heading);
    }

    internal DataType Tupof(DataType type) {
      return DataTypeTuple.Get(type.Heading);
    }
    internal DataType Relof(DataType type) {
      return DataTypeRelation.Get(type.Heading);
    }

    ///============================================================================================
    /// Type checking
    /// 

    // calculate return type given a method and arguments
    // returns true if type error detected 
    public void CheckTypeError(Symbol symbol, out DataType datatype, out CallInfo callinfo, params DataType[] datatypes) {
      datatype = symbol.DataType;
      callinfo = symbol.CallInfo;
      var nargs = symbol.NumArgs; // how many to check
      if (!(datatypes.Length == nargs))
        Parser.ParseError("'{0}' expected {1} arguments, found {2}", symbol.Name, nargs, datatypes.Length);
      var match = symbol.IsCompareOp && datatypes[0] == datatypes[1];
      var hasoverloads = symbol.CallInfo.OverLoad != null;
      for (var cinf = symbol.CallInfo; cinf != null && !match; cinf = cinf.OverLoad) {
        var argts = cinf.Arguments;
        match = Enumerable.Range(0, nargs).All(x => argts[x].DataType.IsTypeMatch(datatypes[x]));
        if (match) {
          callinfo = cinf;
          if (hasoverloads)   // assume symbol table correct unless using overloads
            datatype = cinf.ReturnType; //FIX: bad feeling about this
          else if (datatype == DataTypes.Ordered)
            datatype = datatypes[0];  // FIX: ouch
        }
      }
      if (!match)
        Parser.ParseError("'{0}' type mismatch", symbol.Name);
      if (symbol.IsDyadic)
        CheckDyadicType(symbol.MergeOp, datatypes[0], datatypes[1], ref datatype);
      if (datatype == DataTypes.Table && nargs >= 1 && datatypes[0] is DataTypeRelation)
        datatype = datatypes[0];
      Logger.Assert(datatype.Flags.HasFlag(TypeFlags.Variable) || datatype == DataTypes.Unknown || datatype == DataTypes.Void, datatype.Name);
    }

    void CheckDyadicType(MergeOps mops, DataType reltype1, DataType reltype2, ref DataType datatype) {
      if (!(reltype1 is DataTypeRelation && reltype2 is DataTypeRelation))
        Parser.ParseError("relational arguments expected");
      var cols = DataColumn.Merge(mops, reltype1.Heading.Columns, reltype2.Heading.Columns);
      var dups = cols.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
      if (dups.Length > 0)
        Parser.ParseError("duplicate attribute: {0}", String.Join(",", dups));
      datatype = DataTypeRelation.Get(DataHeading.Create(cols));
    }

    public void CheckTypeMatch(DataType type1, DataType type2) {
      if (!type1.IsTypeMatch(type2)) Parser.ParseError("type mismatch");
    }

  }
}
