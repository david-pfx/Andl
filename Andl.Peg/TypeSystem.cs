using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Andl.Runtime;

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
    internal DataType Relof(DataType type) {
      return DataTypeRelation.Get(type.Heading);
    }
    internal DataType Relof(DataHeading heading) {
      return DataTypeRelation.Get(heading);
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
      if (symbol.IsCompareOp && datatypes[0] != datatypes[1])
        Parser.ParseError("'{0}' arguments must be same type", symbol.Name);
      var match = false;
      var hasoverloads = symbol.CallInfo.OverLoad != null;
      for (var cinf = symbol.CallInfo; cinf != null && !match; cinf = cinf.OverLoad) {
        var argts = cinf.Arguments;
        match = Enumerable.Range(0, nargs).All(x => TypeMatch(argts[x].DataType, datatypes[x]));
        //match = Enumerable.Range(0, nargs).All(x => argts[x].DataType.IsTypeMatch(datatypes[x]));
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
      if (nargs >= 1 && (datatype == DataTypes.Table && datatypes[0] is DataTypeRelation
                         || datatype == DataTypes.Unknown))
        datatype = datatypes[0];
      if (datatype == DataTypes.Table || datatype == DataTypes.Unknown) Parser.ParseError("cannot determine return type: {0}", symbol.Name);
      Logger.Assert(datatype.Flags.HasFlag(TypeFlags.Variable) || datatype == DataTypes.Void, datatype.Name);
    }

    // check dyadic ops and compute result type
    void CheckDyadicType(MergeOps mops, DataType reltype1, DataType reltype2, ref DataType datatype) {
      if (!(reltype1 is DataTypeRelation && reltype2 is DataTypeRelation))
        Parser.ParseError("relational arguments expected");
      var cols = DataColumn.Merge(mops, reltype1.Heading.Columns, reltype2.Heading.Columns);
      var dups = cols.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
      if (dups.Length > 0)
        Parser.ParseError("duplicate attribute: {0}", String.Join(",", dups));
      datatype = DataTypeRelation.Get(DataHeading.Create(cols));
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
