using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    public DataType Type { get; set; }
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

    // just a set of statements
    public AstBlock Block(IList<AstStatement> statements) {
      return new AstBlock { Statements = statements.ToArray() };
    }

    public AstOpCall Transform(AstValue where, IList<AstOrder> order, bool allbut, IList<AstProject> attrib) {
      var args = new AstValue[] {
          (where == null) ? null : where,
          (order == null) ? null : Orderer(order.ToArray()),
          (attrib == null) ? null : Projector(attrib.ToArray(), allbut),
        };
      return OpCall(":transform", args);
    }

    public AstValue DoBody(IList<AstStatement> statements) {
      return new AstValue();
    }

    public AstDefine UserType(string ident, AstField[] fields) {
      return new AstUserType {
        Name = ident,
        Fields = fields
      };
    }
    public AstDefine SubType(string ident, AstType super) {
      return new AstSubType {
        Name = ident,
        Type = super.DataType
      };
    }
    public AstDefine Source(string ident, AstValue value) {
      return new AstSource {
        Name = ident,
        Value = value,
      };
    }
    public AstDefine Deferred(string ident, AstType type, IList<AstField> args, AstStatement body) {
      return new AstDefer {
        Name = ident,
        Type = (type == null) ? null : type.DataType, // FIX
        Fields = (args == null) ? null : args.ToArray(),
        Value = body,
      };
    }

    public AstStatement Assignment(string ident, AstValue value) {
      return new AstAssign { Name = ident, Value = value };
    }
    public AstStatement UpdateJoin(string ident, string op, AstValue expr) {
      return FunCall(":upjoin", Variable(ident), OpCall(op, expr)); // wrong
    }
    public AstStatement UpdateTransform(string ident, AstOpCall transform) {
      return FunCall(":uptrans", Variable(ident), transform); // wrong
    }

    public AstField Field(string ident, AstType type) {
      return new AstField() {
        Name = ident,
        Type = (type == null) ? Types.Find("text") : type.DataType
      };
    }
    public AstProject Project(string name, string rename = null, AstValue value = null) {
      if (name == null) return new AstLift { Value = value };
      if (rename != null) return new AstRename { Name = name, OldName = rename };
      if (value != null) return new AstExtend { Name = name, Value = value };
      return new AstProject { Name = name };
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
        : rows != null ? rows[0].DataType
        : Types.Find("relation");
      if (header != null)
        foreach (var r in rows) r.DataType = type;
      return FunCall(":table", type, rows == null ? null : rows.ToArray());
    }
    public AstValue Table(bool star) {
      var ret = FunCall(":tablestar", Types.Find("relation")); // FIX: requires inherited heading
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
      return new AstExpression {
        Value = value,
        OpCalls = ops.ToArray(), // needs to be sorted by precedence
      };
    }

    public AstValue FunCall(string name, DataType type, params AstValue[] args) {
      var op = FindOperator(name);
      return new AstFunCall { Operator = op, Arguments = args, DataType = type };
    }
    public AstValue FunCall(string name, params AstValue[] args) {
      var op = FindOperator(name);
      return new AstFunCall { Operator = op, Arguments = args, DataType = op.ReturnType };
    }
    public AstOpCall OpCall(string name, params AstValue[] args) {
      var op = FindOperator(name);
      return new AstOpCall() { Operator = op, Arguments = args, DataType = op.ReturnType };
    }
    public AstDefCall DefCall(string name, params AstField[] args) {
      var op = FindOperator(name);
      return new AstDefCall { Operator = op, Arguments = args, DataType = op.ReturnType };
    }

    ///--------------------------------------------------------------------------------------------
    /// Variables and literals
    /// 
    public AstVariable Variable(string name) {
      var v = FindVariable(name);
      return new AstVariable { Variable = v, DataType = v.DataType };
    }

    public AstValue Literal<T>(string type, T value) {
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
    public AstValue Text(string value) {
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

    public Symbol FindVariable(string name) {
      var ret = Syms.Find(name);
      for (; ret == null; ret = Syms.Find(name))
        Syms.Add(name, Types.Find("text"));
      return ret;
    }
    public OpSymbol FindOperator(string name) {
      var ret = Syms.Find(name);
      for (; ret == null; ret = Syms.Find(name))
        Syms.AddOperator(name, Types.Find("code"), Types.Find("text"));
      return ret as OpSymbol;
    }
    //public AstType Typeof(DataType type) {
    //  return new AstType { DataType = type };
    //}
    // get a heading type
    public DataType Typeof(IEnumerable<AstField> fields) {
      var typelist = fields.Select(f => new Field { Name = f.Name, Type = f.Type });
      return Types.Find(typelist);
    }
  }
}
