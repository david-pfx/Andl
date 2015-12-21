using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Andl.Runtime;

namespace Andl.Peg {
  public class CodeValue { }

  /// <summary>
  /// Base class for AST nodes
  /// </summary>
  public class AstNode {
  }
  public class AstType : AstNode {
    public DataType DataType { get; set; }
  }
  // A statement also can be a define or a value/expression
  public class AstStatement : AstNode { }

  /// <summary>
  /// Base class for AST definitions
  /// </summary>
  public class AstDefine : AstStatement {
    public string Name { get; set; }
    public DataType Type { get; set; }
    public AstField[] Fields { get; set; }
    public AstStatement Value { get; set; }
  }
  public class AstUserType : AstDefine { }
  public class AstSubType : AstDefine { }
  public class AstDefer : AstDefine { }
  public class AstSource : AstDefine { }
  public class AstAssign : AstDefine { }

  /// <summary>
  /// Base class for AST field info
  /// </summary>
  public class AstField : AstNode {
    public string Name { get; set; }
    public DataType DataType { get; set; }
  }
  public class AstProject : AstField { }
  public class AstRename : AstProject {
    public string OldName { get; set; }
  }
  public class AstExtend : AstProject {
    public AstValue Value { get; set; }
  }
  public class AstLift : AstExtend { }
  public class AstOrder : AstProject {
    public bool Desc { get; set; }
    public bool Group { get; set; }
  }

  /// <summary>
  /// Base class for AST values
  /// </summary>
  public class AstValue : AstStatement {
    public DataType DataType { get; set; }
  }
  public class AstOperator : AstValue {
    public Symbol Symbol { get; set; }
  }
  public class AstVariable : AstValue {
    public Symbol Variable { get; set; }
  }
  public class AstLiteral<T> : AstValue {
    public T Value { get; set; }
  }

  public class AstBlock : AstValue {
    public AstStatement[] Statements { get; set; }
  }
  public class AstExpression : AstValue {
    public AstValue Value { get; set; }
    public AstOpCall[] OpCalls { get; set; }
  }
  public class AstFunCall : AstValue {
    public Symbol Operator { get; set; }
    public AstValue[] Arguments { get; set; }
  }
  public class AstOpCall : AstValue {
    public Symbol Operator { get; set; }
    public AstValue[] Arguments { get; set; }
  }
  public class AstDefCall : AstValue {
    public Symbol Operator { get; set; }
    public AstField[] Arguments { get; set; }
  }
  public class AstOrderer : AstValue {
    public AstOrder[] Elements { get; set; }
  }
  public class AstProjector : AstValue {
    public bool Allbut { get; set; }
    public AstProject[] Elements { get; set; }
  }

  ///==============================================================================================
  /// <summary>
  /// Implement factory for AST nodes
  /// </summary>
  public class AstFactory {
    public TypeSystem Types { get; set; }
    public SymbolTable Syms { get; set; }
    public Catalog Cat { get; set; }

    // just a set of statements
    public AstBlock Block(IList<AstStatement> statements) {
      return new AstBlock { Statements = statements.ToArray() };
    }

    public AstValue DoBody(IList<AstStatement> statements) {
      return new AstValue();
    }

    public AstDefine UserType(string ident, AstField[] fields) {
      var ff = fields.Select(a => DataColumn.Create(a.Name, a.DataType)).ToArray();
      var ut = DataTypeUser.Get(ident, ff);
      Syms.AddUserType(ident, ut);
      return new AstUserType {
        Name = ident,
        Fields = fields
      };
    }
    public AstDefine SubType(string ident, AstType super) {
      var cols = new DataColumn[] { DataColumn.Create("super", super.DataType) };
      var ut = DataTypeUser.Get(ident, cols);
      Syms.AddUserType(ident, ut);
      return new AstSubType {
        Name = ident,
        Type = super.DataType
      };
    }
    public AstDefine Source(string ident, AstLiteral<string> value) {
      var datatype = Cat.GetRelvarType(ident, value.Value);
      Syms.AddVariable(ident, datatype, SymKinds.CATVAR);
      return new AstSource {
        Name = ident,
        Value = value,
      };
    }
    public AstDefine Deferred(string ident, AstType rettype, IList<AstField> arguments, AstStatement body) {
      return new AstDefer {
        Name = ident,
        Type = (rettype == null) ? null : rettype.DataType, // FIX
        Fields = (arguments == null) ? null : arguments.ToArray(),
        Value = body,
      };
    }

    public AstStatement Assignment(string ident, AstValue value) {
      Syms.AddVariable(ident, value.DataType, SymKinds.CATVAR);
      return new AstAssign { Name = ident, Value = value };
    }
    public AstStatement UpdateJoin(string ident, string op, AstValue expr) {
      return FunCall(":upjoin", OpCall(op, expr)); // FIX:
      //return FunCall(":upjoin", FindCatVar(ident), OpCall(op, expr)); // FIX:
    }
    public AstStatement UpdateTransform(string ident, AstValue[] args) {
      var argx = new List<AstValue>(args);
      //argx.Insert(0, FindCatVar(ident));
      return FunCall(":uptrans", argx.ToArray());
      //return FunCall(":uptrans", Variable(ident), transform); // FIX:
    }
    public AstValue Transform(AstValue rel, AstValue[] args) {
      var argx = new List<AstValue>(args);
      argx.Insert(0, rel);
      return FunCall("transform", argx.ToArray());
    }
    public AstValue[] TransformTail(AstValue where, IList<AstOrder> order, bool allbut, IList<AstProject> attrib) {
      var args = new AstValue[] {
          (where == null) ? null : where,
          (order == null) ? null : Orderer(order.ToArray()),
          (attrib == null) ? null : Projector(attrib.ToArray(), allbut),
        };
      return args;
    }


    public AstField Field(string ident, AstType type) {
      return new AstField() {
        Name = ident,
        DataType = (type == null) ? Types.Find("text") : type.DataType
      };
    }
    public AstProject Project(string name, string rename = null, AstValue value = null) {
      if (name == null) return new AstLift { Value = value };
      if (rename != null) return new AstRename { Name = name, OldName = rename, DataType = FindField(rename).DataType };
      if (value != null) return new AstExtend { Name = name, Value = value, DataType = value.DataType };
      return new AstProject { Name = name, DataType = FindField(name).DataType };
    }
    public AstOrder Order(string name, bool desc, bool group) {
      return new AstOrder { Name = name, Desc = desc, Group = group };
    }

    public AstValue Row(IList<AstProject> fields) {
      return FunCall(":rowf", Typeof(fields), Projector(fields.ToArray()));
    }
    public AstValue Row(IList<AstValue> values) {
      return FunCall(":rowv", Types.Find("tuple"), values.ToArray());  // FIX: requires inherited heading
    }
    public AstValue RowStar() {
      return FunCall(":rowstar", Types.Find("tuple"));  // FIX: requires inherited heading
    }

    public AstValue Table(IList<AstField> header, IList<AstValue> rows) {
      var type = (header != null) ? Typeof(header)
        : rows != null ? Types.Relof(rows[0].DataType)
        : DataTypeRelation.Empty;
      if (header != null)
        foreach (var r in rows) r.DataType = Types.Tupof(type);
      return FunCall(":table", type, rows == null ? null : rows.ToArray());
    }
    public AstValue Table(bool star) {
      var ret = FunCall(":tablestar", DataTypeRelation.Empty); // FIX: requires inherited heading
      return ret;
    }

    ///--------------------------------------------------------------------------------------------
    /// Headings etc
    /// 

    public AstValue Orderer(AstOrder[] args) {
      return new AstOrderer { Elements = args, DataType = Typeof(args) };
    }
    public AstValue Projector(AstProject[] args, bool allbut = false) {
      return new AstProjector { Elements = args, Allbut = allbut, DataType = Typeof(args) };
    }

    ///--------------------------------------------------------------------------------------------
    /// Calls with arguments
    /// 
    public AstValue Expression(AstValue value, IList<AstOpCall> ops) {
      if (ops.Count == 0) return value;
      var opa = ops.ToArray(); // needs to be sorted by precedence
      var dt = opa.Last().DataType;
      return new AstExpression {
        Value = value,
        OpCalls = opa, 
        DataType = dt
      };
    }

    public AstValue FunCall(string name, DataType type, params AstValue[] args) {
      var op = FindOperator(name);
      return new AstFunCall { Operator = op, Arguments = args, DataType = type };
    }
    public AstValue FunCall(string name, params AstValue[] args) {
      var op = FindOperator(name);
      var type = (args.Length > 0) ? args[0].DataType : op.DataType;
      return new AstFunCall { Operator = op, Arguments = args, DataType = type };
    }
    public AstOpCall OpCall(string name, params AstValue[] args) {
      var op = FindOperator(name);
      var type = (args.Length > 0) ? args[0].DataType : op.DataType;
      return new AstOpCall() { Operator = op, Arguments = args, DataType = type };
    }
    public AstDefCall DefCall(string name, params AstField[] args) {
      var op = FindOperator(name);
      var type = (args.Length > 0) ? args[0].DataType : op.DataType;
      return new AstDefCall { Operator = op, Arguments = args, DataType = type };
    }
    public AstValue[] ValueList(params AstValue[] args) {
      return args;
    }

    ///--------------------------------------------------------------------------------------------
    /// Variables and literals
    /// 
    public AstVariable Variable(string name) {
      var v = FindVariable(name);
      return new AstVariable { Variable = v, DataType = v.DataType };
    }

    public AstLiteral<T> Literal<T>(string type, T value) {
      return new AstLiteral<T> { Value = value, DataType = Types.Find(type) };
    }

    public AstValue Binary(string value) {
      var b = new byte[value.Length / 2];
      for (var i = 0; i < b.Length; ++i) {
        int n;
        if (!Int32.TryParse(value.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n))
          return null;
        b[i] = (byte)n;
      }
      return Literal("binary", b);

    }
    public AstValue Bool(string value) {
      return Literal("bool", value[0] == 't');
    }
    public AstValue Number(string value) {
      decimal dret;
      if (Decimal.TryParse(value, out dret))
        return Literal("number", dret);
      return null;
    }
    public AstLiteral<string> Text(string value) {
      return Literal("text", value);
    }
    public AstValue Time(string value) {
      DateTime tret;
      if (DateTime.TryParse(value, out tret))
        return Literal("time", tret);
      return null;
    }

    ///--------------------------------------------------------------------------------------------
    ///  Utility
    /// 
    public AstType FindType(string name) {
      return new AstType { DataType = Types.Find(name) };
    }
    public AstType Typeof(AstValue value) {
      return new AstType { DataType = value.DataType };
    }

    //public bool IsCatVar(string name) { return FindCatVar(name) != null; }
    //public bool IsField(string name) { return FindField(name) != null; }
    //public bool IsVariable(string name) { return FindVariable(name) != null; }
    //public bool IOperator(string name) { return FindOperator(name) != null; }

    public Symbol FindCatVar(string name) {
      var ret = Syms.FindIdent(name);
      return ret != null && ret.IsCatVar ? ret : null;
    }
    public Symbol FindField(string name) {
      var ret = Syms.FindIdent(name);
      return ret != null && ret.IsField ? ret : null;
    }
    public Symbol FindVariable(string name) {
      var ret = Syms.FindIdent(name);
      return ret != null && ret.IsVariable ? ret : null;
    }
    public Symbol FindOperator(string name) {
      var ret = Syms.FindIdent(name);
      return ret != null && ret.IsCallable ? ret : null;
    }
    //public AstType Typeof(DataType type) {
    //  return new AstType { DataType = type };
    //}
    // get a heading type
    public DataType Typeof(IEnumerable<AstField> fields) {
      var typelist = fields.Select(f => new Field { Name = f.Name, Type = f.DataType });
      return Types.Find(typelist);
    }
  }
}
