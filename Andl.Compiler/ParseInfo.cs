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
using Andl.Runtime;

namespace Andl.Compiler {
  ///=================================================================
  ///
  /// Generalised Parse Info
  /// 

  /// <summary>
  /// Capture transform parse information
  /// 
  /// All info is directed towards calculating the value of a single new attribute
  /// </summary>
  /// 
  public class TransformInfo {
    public string CallName { get; private set; }
    public ExpressionBlock[] Restrict { get; private set; }
    public ExpressionBlock[] AttributeExprs { get; private set; }
    public DataHeading Heading { get; private set; }
    public List<OrderInfo> OrderInfo { get; private set; }
    public bool Lift { get; private set; }

    public static TransformInfo Create(ExpressionBlock[] ebs, List<OrderInfo> orderinfo, DataHeading heading) {
      return new TransformInfo {
        Restrict = ebs,
        AttributeExprs = new ExpressionBlock[0],
        OrderInfo = orderinfo,
        Heading = heading,
      };
    }
    public void Update(string callname, bool lift, ExpressionBlock[] atexprs) {
      CallName = callname;
      AttributeExprs = atexprs;
      Lift = lift;
      Heading = DataHeading.Create(atexprs.Select(a => DataColumn.Create(a.Name, a.DataType)).ToArray());
    }
  }

  /// <summary>
  /// Capture attribute terminal information
  /// </summary>
  /// 
  internal struct AttrTermInfo {
    internal ExpressionKinds Kind;
    internal Symbol Sym;
    internal Symbol OldSym;
    internal ExprInfo ExprInfo;
    internal DataType DataType;
    internal DataHeading Heading { get { return DataType.Heading; } }
    internal bool Lift;
    internal DataHeading Lookup;

    internal static AttrTermInfo Create(ExpressionKinds kind, Symbol sym, DataType type) {
      return new AttrTermInfo {
        Kind = kind,
        Sym = sym,
        DataType = type,
      };
    }

    internal static AttrTermInfo Create(ExpressionKinds kind, Symbol sym, Symbol oldsym, DataType type) {
      return new AttrTermInfo {
        Kind = kind,
        Sym = sym,
        OldSym = oldsym,
        DataType = type,
      };
    }

    internal static AttrTermInfo Create(ExprInfo expr, Symbol sym, bool lift = false) {
      return new AttrTermInfo {
        Kind = expr.Kind,
        Sym = sym,
        DataType = expr.DataType,
        ExprInfo = expr,
        Lookup = expr.GetLookup(),
        Lift = lift,
      };
    }

    internal ExpressionBlock Expression() {
      return Kind == ExpressionKinds.Project ? ExpressionBlock.Create(Sym.Name, Sym.Name, DataType)
        : Kind == ExpressionKinds.Rename ? ExpressionBlock.Create(Sym.Name, OldSym.Name, DataType)
        : ExprInfo.Expression(Sym.Name, Lookup);
    }
  };

  /// <summary>
  /// Capture expression parse info
  /// </summary>
  public struct ExprInfo {
    internal Symbol Sym;
    internal ByteCode Code;
    internal DataType DataType;
    internal DataHeading Heading { get { return DataType.Heading; } }
    internal bool IsFolded;     // contains aggregation, so used by FOLD()
    public int AccumCount { get; set; }
    public DataColumn[] LookupItems { get; set; }

    internal bool HasFold { get { return AccumCount > 0; } }      // contains a FOLD(), so uses an accumulator

    internal string Name { get { return Sym != null ? Sym.Name : ":anon"; } }
    internal DataHeading GetLookup() {
      return (LookupItems == null) ? null : DataHeading.Create(LookupItems);
        //: DataHeading.Create(LookupItems.Select(x => DataColumn.Create(x.Name, x.DataType)).ToArray());
    }
    internal ExpressionKinds Kind {
      get {
        Logger.Assert(!(IsFolded && HasFold), Name);
        return (IsFolded) ? ExpressionKinds.IsFolded : (HasFold) ? ExpressionKinds.HasFold : ExpressionKinds.Open;
      }
    }

    public override string ToString() {
      return String.Format("{0}:{1}[{2}]{3}{4} @{5}", Name, DataType, Code.Length,
        IsFolded ? " isfold" : "",
        HasFold ? " hasfold" : "",
        LookupItems != null ? GetLookup().ToString() : "");
    }

    // Create an expression block using the symbol as name
    internal ExpressionBlock Expression(DataHeading lookup = null, bool lazy = false) {
      Logger.Assert(!(IsFolded && HasFold), Name);
      return ExpressionBlock.Create(Name, Kind, Code, DataType, AccumCount, lookup ?? GetLookup(), lazy);
    }
    // Create an expression block with a specified name (specially for fields)
    internal ExpressionBlock Expression(string name, DataHeading lookup = null) {
      Logger.Assert(!(IsFolded && HasFold), name);
      return ExpressionBlock.Create(name, Kind, Code, DataType, AccumCount, lookup);
    }
  };

  /// <summary>
  /// Capture order parse info
  /// </summary>
  public struct OrderInfo {
    internal Symbol Sym;
    internal bool Descending;
    internal bool Grouped;

    // create an expression for this info
    internal ExpressionBlock Expression() {
      return ExpressionBlock.Create(Sym.Name, Sym.DataType, Grouped, Descending);
    }
  };

  /// <summary>
  /// Implement storage for aggregation and ordering info
  /// 
  /// Each expression is evaluated iteratively and used to update the corresponding accumulator
  /// </summary>
  public class AccumulatorInfo {
    // link to previous as stack
    public AccumulatorInfo Parent { get; private set; }
    // number of accumulators used
    public int AccumCount { get; set; }

    public bool HasFold { get { return AccumCount > 0; } }

    public static AccumulatorInfo Create(AccumulatorInfo parent) {
      var tei = new AccumulatorInfo {
        AccumCount = 0,
        Parent = parent,
      };
      return tei;
    }

  }

}
