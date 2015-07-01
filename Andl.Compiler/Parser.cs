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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Andl.Runtime;

namespace Andl.Compiler {
  ///=================================================================
  /// Compiler
  /// 
  /// Recursive descent parser.
  /// Each ParseXxx function tries to match a specific construct. It returns true if it matches, false if not.
  /// 
  public class Parser : IDisposable {
    // get whether error happened on this statement
    public bool Error { get; private set; }
    // get or set debug level
    public int ErrorCount { get; private set; }
    // get instance of symbol table
    public SymbolTable SymbolTable { get; private set; }
    // get set instance of Catalog
    public Catalog Catalog { get; private set; }

    // Lexer instance
    Lexer _lexer;
    // parser stack
    Stack<Symbol> _symstack = new Stack<Symbol>();
    //// scope stack
    Stack<DataType> _typestack = new Stack<DataType>();

    // code emitter
    Emitter _emitter;
    // evaluator, in case we need it
    Evaluator _evaluator;

    // Accumulator info is discovered during expression parsing and set globally
    AccumulatorInfo _accuminfo;

    //------------------------------------------------------------------

    protected virtual void Dispose(bool disposing) {
      if (disposing) _emitter.Dispose();
    }

    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    // Factory method
    public static Parser Create(Catalog catalog, Evaluator evaluator) {
      return new Parser() {
        SymbolTable = SymbolTable.Create(catalog),
        Catalog = catalog,
        _evaluator = evaluator,
      };
      // TODO: add symbols from catalog
    }

    // Load a file from a reader
    // Compile and execute line by line
    public bool Compile(TextReader reader, PersistWriter writer = null) {
      _lexer = Lexer.Create(reader, SymbolTable, Catalog);
      _emitter = new Emitter();
      Error = false;

      // only when everything is ready -- process initial directives to set flags
      TakeEol();
      Catalog.Start();
      SymbolTable.Add(Catalog, ScopeLevels.Global);
      SymbolTable.Add(Catalog, ScopeLevels.Persistent);
      
      // main parser
      if (!ParseMain() || ErrorCount > 0) return ErrorCount == 0;

      var code = _emitter.GetCode();
      // decode/execute once at end?
      if (Logger.Level >= 3 && !Catalog.InteractiveFlag)
          Decoder.Create(code).Decode();
      // OBS: write code to persistent form
      if (writer != null) {
        var eb = ExpressionBlock.Create("main", ExpressionKinds.Closed, code, DataTypes.Void);
        writer.Store(CodeValue.Create(eb)); //FIX: CodeValue may not work
      }
      // batch execution
      if (!Catalog.InteractiveFlag && _evaluator != null) {
        Logger.WriteLine(3, "Begin execution");
        _evaluator.Exec(code);
      }
      return true;
    }

    // Parse a sequence of statements
    bool ParseMain() {
      var ret = false;
      while (!Check(Atoms.EOF)) {
        Error = false;
        var marker = _emitter.GetMarker();
        var datatype = DataTypes.Void;
        var result = ParseStatement(out datatype);

        // wrap return value so it will print out
        if (result && datatype != DataTypes.Void) {
          _emitter.OutCall(SymbolTable.Find("pp"));
          _emitter.OutCall(SymbolTable.Find("write"));
        }
        var code = _emitter.GetSeg(marker, true);
        if (Logger.Level >= 3)
          Decoder.Create(code).Decode();
        // are there unparsed tokens on the line?
        if (!Check(Atoms.EOL) && !Check(Atoms.EOF)) {
          if (!Error)
            ErrSyntax("unknown syntax error at {0}", Look().Name);
          // skip them
          while (!(Check(Atoms.EOL) || Check(Atoms.EOF)))
            Take();
        }
        // shall we execute?
        if (Error) ErrorCount++;
        else if (result && Catalog.InteractiveFlag && _evaluator != null)
          _evaluator.Exec(code);
        ret = true;
      }
      return ret;
    }

    // stmt := assign | update | expression
    // If not a statement bare expression allowed too
    bool ParseStatement(out DataType datatype) {
      TakeEol();
      datatype = DataTypes.Void;
      return ParseDeferred()
        || ParseDecls()
        || ParseUpdateJoin()
        || ParseUpdateTransform()
        || ParseAssignment()      // after updates to resolve ambiguity
        || ParseExpression(out datatype);
    }

    //------------------------------------------------------------------
    // Parse one or more declarations separated by commas
    bool ParseDecls() {
      if (!Match(Atoms.DEF)) return false;
      DataType datatype;
      Symbol idsym;
      do {
        if (ParseUserType(out datatype) || ParseConnect(out datatype)) {

        } else if (ParseDecl(out idsym)) {  //FIX: what does this mean?

        } else return ErrExpect("declaration");
      } while (Match(Atoms.SEP));
      return true;
    }

    //------------------------------------------------------------------
    // Define a name with a value, which will be evaluated immediately
    // assign ::= ident := scoped expr
    bool ParseAssignment() {
      if (!(Look(1).Atom == Atoms.LA)) return false;
      ExprInfo expr;
      if (!ParseNamedExpression(out expr)) return ErrExpect("named expression");
      if (Error) return true;

      if (expr.Sym.Kind == SymKinds.CATVAR) {
        if (!(expr.Sym.DataType == expr.DataType || expr.DataType == DataTypes.Unknown))
          return ErrSyntax("type mismatch: {0}", expr.Sym.Name);
      }  else if (!expr.Sym.IsDefinable) {
        return ErrSyntax("already defined: {0}", expr.Sym.Name);
      } else {
        expr.Sym = SymbolTable.MakeCatVar(expr.Sym.Name, expr.DataType);
        Scope.Current.Add(expr.Sym);
        SymbolTable.AddCatalog(expr.Sym);
      }
      _emitter.OutSeg(expr.Expression());
      _emitter.OutCall(SymbolTable.Find(Symbol.Assign));
      return true;
    }

    //------------------------------------------------------------------
    // Define a name with a deferred value, possibly with arguments
    // deferred ::= ident [: type] [ ( args SEP ) ] => expr
    bool ParseDeferred() {
      if (!(Look().Atom == Atoms.IDENT)) return false;
      if (!(Look(1).Atom == Atoms.RA
         || Look(1).Atom == Atoms.COLON
         || (Look().IsUndefIdent && Look(1).Atom == Atoms.LP)))
        return false;
      var idsym = Symbol.None;
      if (!ParseDecl(out idsym)) return ErrSyntax("already defined: {0}", idsym.Name);

      var args = new List<Symbol>();
      if (Match(Atoms.LP)) {
        Scope.Push();
        ParseDeclList(args);
        Scope.Pop();
        if (!Match(Atoms.RP)) return ErrCheck(Atoms.RP);
      }
      if (!Match(Atoms.RA)) return ErrExpect(Atoms.RA);
      
      // Expression needs both function and args in scope
      // set up function because it might get called recursively

      idsym = SymbolTable.MakeDeferred(idsym.Name, idsym.DataType, MakeColumns(args));
      Scope.Current.Add(idsym);

      Scope.Push();
      foreach (var sym in args)
        Scope.Current.Add(sym);
      ExprInfo expr;
      var marker = _emitter.GetMarker();
      if (ParseUpdateJoin() || ParseUpdateTransform()) {
        // parse statements allowed without a do block
        expr = new ExprInfo {
          Sym = idsym,
          Code = _emitter.GetSeg(marker),
          DataType = DataTypes.Void,
        };
      } else {
        // Parse an open attribute expression that may use accumulators
        // any statements must be wrapped in a do block
        _accuminfo = AccumulatorInfo.Create(_accuminfo);
        var naccum = _accuminfo.AccumCount;
        var ok = ParseOpenAttrExpression(out expr);
        idsym.CallInfo.AccumCount = _accuminfo.AccumCount - naccum;
        _accuminfo = _accuminfo.Parent;
        if (!ok) return ErrExpect("expression");
        expr.Sym = idsym;
      }
      if (Error) return true;
      Scope.Pop();

      // Update type -- could still be unknown if lazy
      if (idsym.DataType == DataTypes.Unknown)
        idsym.DataType = expr.DataType;
      else if (!expr.DataType.IsTypeMatch(idsym.DataType)) return ErrExpect(idsym.DataType.ToString() + " expression");

      // add to catalog now type is known
      SymbolTable.AddCatalog(idsym);

      var argtype = DataHeading.Create(idsym.CallInfo.Arguments);
      var eblock = expr.Expression(argtype, true);
      // output wrapped as code value to suppress execution
      _emitter.OutLoad(CodeValue.Create(eblock));
      _emitter.OutCall(SymbolTable.Find(Symbol.Defer));
      return true;
    }

    //------------------------------------------------------------------
    // upjoin ::= rel-id := dyadic-op rel-expr
    bool ParseUpdateJoin() {
      if (!(Look().IsIdent && Look(1).Is(Atoms.LA) && Look(2).IsDyadic)) return false;

      var varsym = Take();
      if (!(varsym.Kind == SymKinds.CATVAR && varsym.DataType is DataTypeRelation)) 
        return ErrExpect("relational variable");
      _emitter.OutName(Opcodes.LDCAT, varsym);

      Match(Atoms.LA);
      var joinsym = Take();
      if (joinsym.JoinOp == JoinOps.NUL) return ErrExpect("joinable operator");

      DataType datatype;
      if (!(ParseExpression(out datatype) && datatype.Equals(varsym.DataType)))
        return ErrSyntax("relational expression with same heading expected");

      _emitter.OutLoad(NumberValue.Create((int)joinsym.JoinOp));
      _emitter.OutCall(SymbolTable.Find(Symbol.UpdateJoin));
      return true;
    }

    //------------------------------------------------------------------
    // uptrn ::= rel-id := trn-op
    bool ParseUpdateTransform() {
      if (!(Look().IsIdent && Look(1).Is(Atoms.LA) && Look(2).Is(Atoms.LB))) return false;
      var varsym = Take();
      if (!(varsym.Kind == SymKinds.CATVAR && varsym.DataType is DataTypeRelation))
        return ErrExpect("relational variable");

      Match(Atoms.LA);
      var datatype = varsym.DataType;
      TransformInfo trinfo;
      if (!ParseTransformTail(datatype, out trinfo)) return ErrExpect("transform");
      if (!(trinfo.CallName == null || trinfo.CallName == Symbol.Transform))
        return ErrSyntax("invalid transform term for update");
      if (!(trinfo.Heading.Equals(varsym.DataType.Heading)))
        return ErrSyntax("invalid heading for update");

      _emitter.OutName(Opcodes.LDCAT, varsym);
      if (trinfo.Restrict == null)
        _emitter.OutSeg(ExpressionBlock.True);
      else _emitter.OutSegs(trinfo.Restrict);
      _emitter.OutSegs(trinfo.AttributeExprs);
      _emitter.OutCall(SymbolTable.Find(Symbol.UpdateTransform), trinfo.AttributeExprs.Length);
      return true;
    }

    //------------------------------------------------------------------
    // Parse an expression using a stack based parser to handle precedence
    // expr ::= primary { BINOP primary }
    // Must ensure lasttype/heading set for caller to use [there should be a better way]
    bool ParseExpression(out DataType datatype) {
      if (!ParsePrimary(out datatype))
        return false;
      var symbol = Symbol.Mark;
      while (!Error) {
        // experimental: look past EOL to BINOP
        if (LookOverEol(1).Kind == SymKinds.BINOP)
          TakeEol();
        if (Look().Kind == SymKinds.BINOP && (symbol.Atom == Atoms.MARK || symbol.Precedence < Look().Precedence)) {
          _typestack.Push(datatype);
          _symstack.Push(symbol);
          symbol = Take();
          if (!(ParsePrimary(out datatype)))
            return ErrExpect("primary");
        } else {
          if (symbol.Atom == Atoms.MARK)
            break;
          CallInfo callinfo;
          CheckTypeError(symbol, out datatype, out callinfo, _typestack.Pop(), datatype);
          _emitter.OutCall(symbol, 0, callinfo);
          symbol = _symstack.Pop();
        }
      }
      return true;
    }

    //------------------------------------------------------------------
    // Primary :== simp-prim { trn-op|dot-op }
    bool ParsePrimary(out DataType datatype) {
      if (!ParseSimplePrimary(out datatype)) return false;
      while (!Error &&
        (ParseTransform(ref datatype)
        || ParseRecurse(ref datatype)
        || ParseDot(ref datatype))) { }
      return true;
    }

    // Simp-prim :== id-val
    //           | lit-val
    //           | rel-val
    //           | func-call 
    //           | un-op primary
    //           | do { xxx }
    //           | LP expr RP
    bool ParseSimplePrimary(out DataType datatype) {
      datatype = DataTypes.Unknown;
      TakeEol();
      if (ParseDoBlock(out datatype)) return true;
      if (Match(Atoms.LP)) {
        if (ParseExpression(out datatype) && Error) return true;
        if (!Match(Atoms.RP)) return ErrCheck(Atoms.RP);
        return true;
      }

      if (ParseTableOrRow(out datatype)
        || ParseUnop(out datatype)
        || ParseFold(out datatype)
        || ParseIf(out datatype)
        || ParseValue(out datatype)
        || ParseInvoke(out datatype)
        || ParseFunction(out datatype)) // last
        return true;

      // Nothing special, just a value
      var symbol = Look();
      switch (symbol.Kind) {
      case SymKinds.LITERAL:
        _emitter.OutLoad(symbol.Value);
        datatype = symbol.DataType;
        Match();
        return true;
      case SymKinds.FIELD:
      case SymKinds.CATVAR:
      case SymKinds.PARAM:
        if (symbol.IsField) Scope.Current.LookupItems.Add(DataColumn.Create(symbol.Name, symbol.DataType));
        _emitter.OutName(symbol.IsLookup ? Opcodes.LDFIELD : Opcodes.LDCAT, symbol);
        datatype = symbol.DataType;
        Match();
        return true;
      case SymKinds.UNDEF:
        Match();
        return ErrSyntax("undefined: {0}", symbol.Name);
      default:
        return false; // dunno
      }
    }

    //------------------------------------------------------------------
    // Parse a unary operator
    // FIX: something better for unary minus
    bool ParseUnop(out DataType datatype) {
      datatype = DataTypes.Unknown;
      if (!Look().IsUnary) return false;

      var symbol = (Check(Atoms.MINUS)) ? SymbolTable.Find("u-") : Look();
      Match();
      ParsePrimary(out datatype);
      CallInfo callinfo;
      CheckTypeError(symbol, out datatype, out callinfo, datatype);
      _emitter.OutCall(symbol, 0, callinfo);
      return true;
    }

    //------------------------------------------------------------------
    // parse a do{} block
    // doblock ::= do '{' { stmts } expr '}'
    bool ParseDoBlock(out DataType datatype) {
      datatype = DataTypes.Void;
      if (!Match(Atoms.DO)) return false;
      if (!Match(Atoms.LC)) return ErrCheck(Atoms.LC);

      // === open scope shared by local variables
      Scope.Push();
      // Must capture expression as eblock to allow a runtime scope to be set up
      var marker = _emitter.GetMarker();
      var exprcount = 0;
      while (!Match(Atoms.RC)) {
        if (!ParseStatement(out datatype)) return ErrExpect("statement or expression");
        if (datatype.IsVariable) {
          if (++exprcount > 1) 
            ErrSyntax("only one expression allowed in do block");
        } else
          if (datatype != DataTypes.Void) return ErrSyntax("bad expression type in do block: {0}", datatype);
      }
      if (Error) return true;
      Scope.Pop();
      // === end scope

      var code = _emitter.GetSeg(marker);
      var expr = new ExprInfo {
        Code = code,
        DataType = datatype,
      };
      var ebs = expr.Expression();
      _emitter.OutSeg(ebs);
      _emitter.OutCall(SymbolTable.Find(Symbol.DoBlock));
      return true;
    }

    // Parse a recursive relational expression
    // TODO: keyword for DEPTH first
    bool ParseRecurse(ref DataType datatype) {
      if (!(LookOverEol().Kind == SymKinds.RECURSE)) return false;
      TakeEol();
      var funcsym = Take();
      if (!(datatype is DataTypeRelation)) return ErrSyntax("recurse invalid for type {0}", datatype);
      if (!Match(Atoms.LP)) return ErrCheck(Atoms.LP);
      //var opsym = Take();
      //if (!(opsym.JoinOp == JoinOps.UNION)) return ErrExpect("union");
      //if (!Match(Atoms.SEP)) return ErrCheck(Atoms.SEP);

      Scope.Push(datatype);
      ExprInfo expr;
      if (!(ParseOpenExpression(out expr) && expr.DataType == datatype))
        return ErrExpect("expression of same type");
      if (!Match(Atoms.RP)) return ErrCheck(Atoms.RP);
      Scope.Pop();

      _emitter.OutLoad(NumberValue.Create(0)); // reserved
      _emitter.OutSeg(expr.Expression());
      _emitter.OutCall(funcsym);
      return true;
    }

    //------------------------------------------------------------------
    // Transform, a combo of restrict, rename, project, extend, aggregate, order, group
    // No attempt to optimise here. Leave that to runtime pipelining.
    // transform ::= '[' [ predicate ] '{' attr-term-list '}' ']'
    bool ParseTransform(ref DataType datatype) {
      TransformInfo trinfo;
      if (!ParseTransformTail(datatype, out trinfo)  || Error) return Error;

      // predicate if specified
      if (trinfo.Restrict.Length > 0) {
        _emitter.OutSegs(trinfo.Restrict);
        _emitter.OutCall(SymbolTable.Find(Symbol.Restrict), trinfo.Restrict.Length);
      }

      // Extended project if specified
      if (trinfo.CallName != null) {
        _emitter.OutSegs(trinfo.AttributeExprs);
        var count = trinfo.AttributeExprs.Count();
        foreach (var ordi in trinfo.OrderInfo)
          _emitter.OutSeg(ordi.Expression());
        count += trinfo.OrderInfo.Count;
        // TODO: check
        _emitter.OutCall(SymbolTable.Find(trinfo.CallName), count);
        datatype = DataTypeRelation.Get(trinfo.Heading);
      }

      // Lift if specified
      if (trinfo.Lift) {
        var sym = SymbolTable.Find(Symbol.Lift);
        datatype = trinfo.Heading.Columns[0].DataType;
        _emitter.OutCall(sym);
      }

      return true;
    }

    //------------------------------------------------------------------
    // Parse a Transform construct, returning the info
    // transform ::= '[' [ predicate ] '{' attr-term-list '}' ']'
    // This is the ONLY place that opens a tuple scope
    bool ParseTransformTail(DataType datatype, out TransformInfo trinfo) {
      trinfo = null;
      if (!MatchOverEol(Atoms.LB)) return false;
      if (!(datatype is DataTypeRelation)) return ErrSyntax("type {0} cannot have transform", datatype);
      // === open a new scope
      Scope.Push(datatype);

      // where order group (in any order)
      var ebs = new List<ExpressionBlock>();
      var orderinfo = new List<OrderInfo>();

      var where = ParseWhere(ebs);
      TakeEol();
      // Parse the order now. Gets both order and group info.
      var order = ParseOrder(orderinfo);
      TakeEol();
      if (Error) return true;
      
      // Now get attributes
      var infill = false;
      var attrs = new List<AttrTermInfo>();
      if (Match(Atoms.LC)) {
        infill = Match(Atoms.STAR);
        TakeEol();
        if (ParseAttributeTermList(ref attrs) && Error) return Error;
        if (!Match(Atoms.RC)) return ErrCheck(Atoms.RC);
      } else if (order)
        infill = true;    // order but not transform means copy all
      if (!Match(Atoms.RB)) return ErrCheck(Atoms.RB);

      // now put it all together for return
      trinfo = TransformInfo.Create(ebs.ToArray(), orderinfo, datatype.Heading);

      // collect the various columns using just string names
      var attrcols = attrs.Select(a => a.Sym.Name);
      var projcols = attrs
        .Where(a => a.Kind == ExpressionKinds.Project)
        .Select(a => a.Sym.Name);
      var oldcols = attrs
        .Where(a => a.Kind == ExpressionKinds.Rename)
        .Select(a => a.OldSym.Name);
      var finalcols = (infill) ? datatype.Heading.Columns.Select(c => c.Name)
          .Union(attrcols)
          .Except(projcols)
          .Except(oldcols).ToArray()
        : attrcols.ToArray();
      
      bool lift = false;
      string funcname = null;
        // Lift has a single expression, and nothing else does.
      if (attrs.Any(a => a.Lift)) {
        if (finalcols.Length != 1) return ErrSyntax("must be exactly one expression for lift");
        lift = true;
      }

      // Analyse the arguments to see which function to call (if any)
      // Rename and Project are all the same, but which depends on how many. 
      // Transform, TransAgg and TransOrd are successively more capable.
      if (order)
        funcname = Symbol.TransOrd;
      else if (finalcols.Length != 0) {
        if (attrs.All(a => a.Kind == ExpressionKinds.Rename || a.Kind == ExpressionKinds.Project))
          funcname = (finalcols.Count() == datatype.Heading.Degree) ? Symbol.Rename 
            : Symbol.Project;
        else funcname = (attrs.Any(a => a.ExprInfo.HasFold)) ? Symbol.TransAgg : Symbol.Transform;
      }
      
      // finally output expressions to match desired heading, infill with project for any missing
      // this guarantees no duplicates in the output.
      if (funcname != null) {
        var exprs = finalcols.Select(c => {
          var att = attrs.Find(a => a.Sym.Name == c);
          if (att.Kind == ExpressionKinds.Nul) {
            var colx = datatype.Heading.FindIndex(c);
            return ExpressionBlock.Create(c, c, datatype.Heading.Columns[colx].DataType);
          } else return att.Expression();
        });
        trinfo.Update(funcname, lift, exprs.ToArray());
      }

      Scope.Pop();
      // === Close scope
      return true;
    }

    //------------------------------------------------------------------
    // Function ::= Id LP ExprList RP
    // Handles both builtins and defined functions
    bool ParseFunction(out DataType datatype) {
      datatype = DataTypes.Unknown;
      if (!(Look().IsFunction && Look(1).Atom == Atoms.LP)) return false;
      var symbol = Take();
      if (symbol.CallKind == CallKinds.SFUNC)
        _emitter.OutLoad(TextValue.Create(symbol.DataType.Name));

      Match(Atoms.LP);
      var datatypes = new List<DataType>();
      ParseExprList(datatypes);
      if (!Match(Atoms.RP)) return ErrCheck(Atoms.RP);

      CallInfo callinfo;
      CheckTypeError(symbol, out datatype, out callinfo, datatypes.ToArray());
      if (symbol.CallKind == CallKinds.SFUNC)
        _emitter.OutCall(SymbolTable.Find(Symbol.UserSelector), datatypes.Count);
      else _emitter.OutCall(symbol, 0, callinfo);
      return true;
    }

    //------------------------------------------------------------------
    // Function ::= Id LP ExprList RP
    // Handles invoking defined functions
    bool ParseInvoke(out DataType datatype) {
      datatype = DataTypes.Unknown;
      if (!(Look().IsDefFunc && Look(1).Atom == Atoms.LP)) return false;
      var funcsym = Take();
      _emitter.OutName(Opcodes.LDCATR, funcsym);

      // give invoke the accumulator block and offset for any accumulators it may use
      _emitter.Out(Opcodes.LDACCBLK);
      _emitter.OutLoad(NumberValue.Create(_accuminfo == null ? -1 : _accuminfo.AccumCount));

      Match(Atoms.LP);
      var datatypes = new List<DataType>();
      ParseExprList(datatypes);
      if (!Match(Atoms.RP)) return ErrCheck(Atoms.RP);

      CallInfo callinfo;
      CheckTypeError(funcsym, out datatype, out callinfo, datatypes.ToArray());
      if (callinfo.HasFold) {
        // folds in this func contribute to overall accum count
        if (_accuminfo == null) return ErrSyntax("fold not allowed here");
        _accuminfo.AccumCount += callinfo.AccumCount;
      }

      var invoke = SymbolTable.Find(Symbol.Invoke);
      _emitter.OutCall(invoke, datatypes.Count, invoke.CallInfo);
      return true;
    }

    //------------------------------------------------------------------
    // Function ::= DOT Id
    // Handles functions with exactly one argument
    // Value of type datatype already on stack
    bool ParseDot(ref DataType datatype) {
      if (!MatchOverEol(Atoms.DOT)) return false;
      var funsym = Take();
      if (funsym.IsDefFunc) {
        _emitter.OutName(Opcodes.LDCATR, funsym);
        CallInfo callinfo;
        CheckTypeError(funsym, out datatype, out callinfo, datatype);
        _emitter.OutCall(SymbolTable.Find(Symbol.Invoke), 1, callinfo);
      } else if (funsym.IsFunction) {
        CallInfo callinfo;
        CheckTypeError(funsym, out datatype, out callinfo, datatype);
        _emitter.OutCall(funsym, 0, callinfo);
      } else { // assume component
        var udt = datatype as DataTypeUser;
        var colx = udt == null ? -1 : udt.Heading.FindIndex(funsym.Name);
        if (udt == null ||  colx == -1) return ErrSyntax("undefined: {0}", funsym.Name);
        _emitter.OutName(Opcodes.LDCOMP, funsym);
        datatype = udt.Heading.Columns[colx].DataType;
      }
      return true;
    }

    //------------------------------------------------------------------
    // If ::= If LP pred-expr SEP expr SEP expr RP
    // First expr always evaluated, then just one of the others
    bool ParseIf(out DataType datatype) {
      datatype = DataTypes.Unknown;
      if (!(Look().Kind == SymKinds.IF)) return false;
      var symbol = Take();

      if (!Match(Atoms.LP)) return ErrCheck(Atoms.LP);
      var datatype1 = DataTypes.Unknown;
      if (!ParseExpression(out datatype1)) return ErrExpect("expression");
      if (!(datatype1 == DataTypes.Bool)) return ErrExpect("logical expression");

      var exprs = new List<ExprInfo>();
      while (Match(Atoms.SEP)) {
        ExprInfo expr;
        if (!ParseOpenExpression(out expr)) return ErrExpect("expression");
        exprs.Add(expr);
      }
      if (!Match(Atoms.RP)) return ErrCheck(Atoms.RP);

      if (!(exprs.Count == 2)) return ErrSyntax("expected 2 expressions");
      datatype = exprs[0].DataType;
      if (!(datatype.Equals(exprs[1].DataType))) return ErrSyntax("type mismatch");

      _emitter.OutSeg(exprs[0].Expression());
      _emitter.OutSeg(exprs[1].Expression());
      _emitter.OutCall(symbol);

      return true;
    }

    //------------------------------------------------------------------
    // Parse a Fold() function
    // Fold ::= FOLD '(' operator ',' expression ')'
    bool ParseFold(out DataType datatype) {
      datatype = DataTypes.Unknown;
      if (!(Look().Kind == SymKinds.FOLD)) return false;
      if (_accuminfo == null) return ErrSyntax("fold not allowed here");
      var foldsym = Take();
      if (!Match(Atoms.LP)) return ErrCheck(Atoms.LP);
      var opsym = Take();
      if (!opsym.IsFoldable) return ErrSyntax("operator {0} is not foldable", opsym.Name);
      if (!Match(Atoms.SEP)) return ErrCheck(Atoms.SEP);

      // parse expression and wrap it in an aggregation
      ExprInfo expr;
      if (!ParseOpenExpression(out expr)) return ErrExpect("expression");
      if (Error) return true;
      if (!Match(Atoms.RP)) return ErrCheck(Atoms.RP);
      WrapAgg(ref expr, opsym);

      // The expression will only be used to fill an accumulator, which
      // transform will collect later. Allocate id and save the expression here.
      // also set scope flag so outer open expression is informed
      expr.IsFolded = true;

      _emitter.Out(Opcodes.LDACCBLK);
      _emitter.OutLoad(NumberValue.Create(_accuminfo.AccumCount++));
      _emitter.OutLoad(expr.DataType.Default());
      _emitter.OutSeg(expr.Expression());
      _emitter.OutCall(foldsym);

      datatype = expr.DataType;    // CHECK: does this work for set ops?
      return true;
    }

    //------------------------------------------------------------------
    // Parse a Value() function
    // Fold ::= VALUE|RANK '(' attribute ',' num-expr ')'
    bool ParseValue(out DataType datatype) {
      datatype = DataTypes.Unknown;
      if (!(Look().Kind == SymKinds.VALUE || Look().Kind == SymKinds.RANK)) return false;
      var funcsym = Take();
      if (!Match(Atoms.LP)) return ErrCheck(Atoms.LP);
      ExprInfo expr;
      if (!ParseOpenExpression(out expr)) return ErrExpect("open expression");
      _emitter.OutSeg(expr.Expression());

      if (!Match(Atoms.SEP)) return ErrCheck(Atoms.SEP);

      DataType ndt;
      if (!ParseExpression(out ndt) && ndt == DataTypes.Number)
        return ErrExpect("numeric expression");
      if (!Match(Atoms.RP)) return ErrCheck(Atoms.RP);

      datatype = (funcsym.Kind == SymKinds.VALUE) ? expr.DataType : DataTypes.Number;
      _emitter.OutCall(funcsym, 0);
      return true;
    }

    //------------------------------------------------------------------
    // order ::= '$' '(' { ident-list } ')'
    bool ParseOrder(List<OrderInfo> sortinfo) {
      if (!Match(Atoms.DOLLAR)) return false;

      if (Match(Atoms.LP)) {
        do {
          Symbol attsym = Symbol.None;
          var grouped = Match(Atoms.PERCENT);
          var descending = Match(Atoms.MINUS);
          if (!(ParseIdent(ref attsym) && attsym.IsField)) return ErrExpect("attribute");
          sortinfo.Add(new OrderInfo {
            Sym = attsym,
            Descending = descending,
            Grouped = grouped,
          });
        } while (Match(Atoms.SEP));
        if (!Match(Atoms.RP)) return ErrCheck(Atoms.RP);
      }
      return true;
    }

    //------------------------------------------------------------------
    // where ::= [?( expr )] bool-expr
    bool ParseWhere(List<ExpressionBlock> exprinfo) {
      if (!Match(Atoms.QUERY)) return false;
      if (!Match(Atoms.LP)) return ErrExpect(Atoms.LP);

      Logger.Assert(Scope.Current.LookupItems.Items.Length == 0);
      ExprInfo expr;
      if (!ParseLookupExpression(out expr) && expr.DataType == DataTypes.Bool) return ErrExpect("predicate expression");
      exprinfo.Add(expr.Expression());

      if (!Match(Atoms.RP)) return ErrExpect(Atoms.RP);
      return true;
    }

    //------------------------------------------------------------------
    // TableOrRow ::= table | row
    // Requires extra lookahead
    bool ParseTableOrRow(out DataType datatype) {
      datatype = DataTypes.Unknown;
      if (!Check(Atoms.LC)) return false;

      // Table has {{, Row only {
      if (LookOverEol(1).Atom == Atoms.LC) {
        if (!ParseTable(out datatype)) return ErrExpect("table literal");
      } else {
        if (!ParseRow(out datatype)) return ErrExpect("row literal");
      }
      return true;
    }

    //------------------------------------------------------------------
    // Table ::= '{' { row-list SEP } '}'
    bool ParseTable(out DataType datatype) {
      datatype = DataTypes.Unknown;
      if (!Match(Atoms.LC)) return false;

      if (ParseTableWithHeading(out datatype))
        return Match(Atoms.RC) || ErrExpect(Atoms.RC);

      var exprs = new List<ExprInfo>();
      if (!ParseOpenExpressionList(exprs) || exprs.Count == 0) return ErrExpect("heading/value or tuple list");
      if (Error) return true;
      if (!Match(Atoms.RC)) return ErrExpect(Atoms.RC);

      var heading = exprs[0].Heading; // if no heading, must be at least one row
      if (!exprs.All(e => e.Heading.Equals(heading)))
        return ErrSyntax("not a valid tuple list");
      var ebs = exprs.Select(e => e.Expression());

      _emitter.OutLoad(HeadingValue.Create(heading));    // FIX: please no headings
      _emitter.OutSegs(ebs);
      _emitter.OutCall(SymbolTable.Find(Symbol.Table), ebs.Count());

      foreach (var eb in ebs)
        Scope.Current.LookupItems.Add(eb.Lookup.Columns);
      datatype = DataTypeRelation.Get(heading);
      return true;
    }

    //------------------------------------------------------------------
    // TableWH ::= hdng {'{' {expr SEP} '}'}
    // hdng ::= '{' {undef-id ':' [type-id|expr] cma-sep}+ '}'
    //        | '{' ':' '}'
    private bool ParseTableWithHeading(out DataType datatype) {
      datatype = DataTypes.Unknown;

      if (!((Look().Atom == Atoms.LC && Look(1).IsIdent && Look(2).Atom == Atoms.COLON)
         || (Look().Atom == Atoms.LC && Look(1).Atom == Atoms.COLON))) return false;

      // take LC, but if not a heading then untake and return false
      Match(Atoms.LC);
      Scope.Push();
      var idents = new List<Symbol>();
      if (!(Match(Atoms.COLON) || ParseDeclList(idents))) return ErrExpect("declaration list");
      Scope.Pop();
      if (!Match(Atoms.RC)) return ErrExpect(Atoms.RC);

      var cols = MakeColumns(idents);
      //var cols = idents.Select(a => DataColumn.Create(a.Name, a.DataType)).ToArray();
      var heading = DataHeading.Create(cols);

      var exprs = new List<ExprInfo>();
      TakeEol();
      while (Match(Atoms.LP)) {
        TakeEol();
        var valexprs = new List<ExprInfo>();
        if (ParseOpenExpressionList(valexprs) && Error) return true;
        if (!Match(Atoms.RP)) return ErrExpect(Atoms.RP);
        if (!(cols.Length == valexprs.Count)) ErrSyntax("wrong no of values");
        else if (!Enumerable.Range(0, cols.Length).All(x => cols[x].DataType == valexprs[x].DataType)) ErrSyntax("type mismatch");
        else {
          var vebs = Enumerable.Range(0, cols.Length).Select(x => valexprs[x].Expression(cols[x].Name));
          var marker = _emitter.GetMarker();
          _emitter.OutLoad(HeadingValue.Create(heading));
          _emitter.OutSegs(vebs);
          _emitter.OutCall(SymbolTable.Find(Symbol.Row), cols.Length);
          exprs.Add(new ExprInfo {
            Code = _emitter.GetSeg(marker),
            DataType = DataTypeTuple.Get(heading),
          });
        }
        if (Error) return true;
        if (!Match(Atoms.SEP)) break;
      }

      var ebs = exprs.Select(e => e.Expression());
      _emitter.OutLoad(HeadingValue.Create(heading));    // FIX: please no headings
      _emitter.OutSegs(ebs);
      _emitter.OutCall(SymbolTable.Find(Symbol.Table), exprs.Count);
      datatype = DataTypeRelation.Get(heading);
      return true;
    }

    //------------------------------------------------------------------
    // Row ::= '{' ident-list LA expr-list '}'
    bool ParseRow(out DataType datatype) {
      datatype = DataTypes.Unknown;
      if (!Match(Atoms.LC)) return false;
      var attrs = new List<AttrTermInfo>();
      var all = Match(Atoms.STAR);
      if (!all && ParseAttributeTermList(ref attrs) && Error)
        return true;
      if (!Match(Atoms.RC)) return ErrCheck(Atoms.RC);

      var ebs = (all) ? MakeProject(Scope.Current.Heading ?? DataHeading.Empty) 
                      : attrs.Select(a => a.Expression());
      var newcols = ebs.Select(e => e.MakeDataColumn());
      var dups = newcols.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
      if (dups.Length > 0)
        return ErrSyntax("duplicate attribute: {0}", String.Join(",", dups));
      var heading = DataHeading.Create(newcols.ToArray());

      _emitter.OutLoad(HeadingValue.Create(heading));
      _emitter.OutSegs(ebs);
      _emitter.OutCall(SymbolTable.Find(Symbol.Row), ebs.Count());

      foreach (var eb in ebs)
        Scope.Current.LookupItems.Add(eb.Lookup.Columns);
      datatype = DataTypeTuple.Get(heading);
      return true;
    }

    //------------------------------------------------------------------
    // Parse lists and things
    //

    //------------------------------------------------------------------
    // parse list of attribute terms
    bool ParseAttributeTermList(ref List<AttrTermInfo> attrs) {
      AttrTermInfo attr = new AttrTermInfo();
      _accuminfo = AccumulatorInfo.Create(_accuminfo);
      int n = 0;
      while (ParseAttributeTerm(ref attr)) {
        ++n;
        attrs.Add(attr);
        if (!Match(Atoms.SEP)) break;
      }
      _accuminfo = _accuminfo.Parent;
      return n > 0;
    }

    //------------------------------------------------------------------
    // Parse an attribute term, checking carefully what form it is in.
    // attr-term  ::= ren-term | proj-term | ext-term | agg-term
    // ren-term	  ::= att-id := att-val
    // proj-term  ::= att-id
    // ext-term   ::= att-id := open-expr
    // agg-term	  ::= att-id := agg-expr 
    bool ParseAttributeTerm(ref AttrTermInfo attr) {
      var sym1 = Symbol.None;
      var sym2 = Symbol.None;
      ExprInfo expr;

      // If there is a LA it must be rename or extend
      if (Look(1).Atom == Atoms.LA) {
        if (!ParseIdent(ref sym1)) return ErrExpect("identifier");
        Match(Atoms.LA);

        // If RHS is a bare field name then it's a rename term
        if (Look(1).Atom == Atoms.SEP || Look(1).Atom == Atoms.RC) { // rename
          if (ParseIdent(ref sym2)) {
            if (sym2.IsField) {
              attr = AttrTermInfo.Create(ExpressionKinds.Rename, sym1, sym2, sym2.DataType);
              //Scope.Current.LookupFields.Add(sym2);
              return true;
            } else Untake();
          }
        } 
        // otherwise it must be an extend or aggregate
        if (!ParseOpenAttrExpression(out expr)) return ErrExpect("expression");
        attr = AttrTermInfo.Create(expr, sym1);
        return true;
      }

      // Otherwise it must be project or lift or nothing
      if (Look(1).Atom == Atoms.SEP || Look(1).Atom == Atoms.RC) { // project
        if (!ParseIdent(ref sym1)) return false; // punt
        if (!(sym1.IsField))
          return ErrSyntax("not defined: {0}", sym1.Name);
        attr = AttrTermInfo.Create(ExpressionKinds.Project, sym1, sym1.DataType);
        //Scope.Current.LookupFields.Add(sym1);
      } else { // lift -- mostly same as Select, but set flag
        if (!ParseOpenAttrExpression(out expr)) return false;
        if (Error) return true;
        attr = AttrTermInfo.Create(expr, sym1, true);
      }
      return true;
    }

    //------------------------------------------------------------------
    // Parse named expression (with lookahead)
    // Type checking left to caller (only used for assign)
    // nam-expr ::= ident LA open-expr
    bool ParseNamedExpression(out ExprInfo expr) {
      expr = new ExprInfo();
      if (!(Look().Atom == Atoms.IDENT && Look(1).Atom == Atoms.LA)) return false;
      var idsym = Symbol.None;
      if (!ParseIdent(ref idsym)) return false;
      Match(Atoms.LA);
      if (!ParseOpenExpression(out expr)) return ErrExpect("expression");
      //if (!(ParseConnect(idsym, out expr)
      //   || ParseOpenExpression(out expr))) return ErrExpect("expression");
      expr.Sym = idsym;
      return true;
    }

    //------------------------------------------------------------------
    // Parse and return a list of open (deferred) expressions
    // exprs ::= { expr SEP }
    bool ParseOpenExpressionList(List<ExprInfo> exprs) {
      ExprInfo expr;
      if (!ParseOpenExpression(out expr))
        return false;
      for (; ; ) {
        exprs.Add(expr);
        if (!Match(Atoms.SEP)) return true;
        if (!ParseOpenExpression(out expr))
          return ErrExpect("expression");
      }
    }

    //------------------------------------------------------------------
    // parse a list of immediate expressions, return just the data type
    // expr-list ::= { [ expr | LC proj-att-list RC ] sep-op }
    bool ParseExprList(List<DataType> datatypes) {
      var datatype = DataTypes.Unknown;
      if (!ParseExpression(out datatype))
        return false;
      datatypes.Add(datatype);
      while (Match(Atoms.SEP)) {
        if (!ParseExpression(out datatype))
          return ErrExpect("expression");
        datatypes.Add(datatype);
      }
      return true;
    }

    //------------------------------------------------------------------
    // parse an attibute expression that may have a fold
    // requires accuminfo to keep track
    bool ParseOpenAttrExpression(out ExprInfo expr) {
      var accums = _accuminfo.AccumCount;
      var ret = ParseLookupExpression(out expr);
      if (ret && !Error && _accuminfo.HasFold)
          expr.AccumCount = _accuminfo.AccumCount - accums;
      return ret;
    }

    //------------------------------------------------------------------
    // parse an open expression that uses a lookup
    bool ParseLookupExpression(out ExprInfo expr) {
      Logger.Assert(Scope.Current.LookupItems.Items.Length == 0);
      var ret = ParseOpenExpression(out expr);
      if (ret && !Error) expr.LookupItems = Scope.Current.LookupItems.Items;
      Scope.Current.LookupItems.Clear();
      return ret;
    }

    //------------------------------------------------------------------
    // parse an expression with a row as context
    // for aggregation bracket with LDAGG and symbol
    bool ParseOpenExpression(out ExprInfo expr) {
      var ret = false;
      var marker = _emitter.GetMarker();
      Logger.WriteLine(3, "OpenExp marker={0} scope={1} -->", marker, Scope.Current.Level);
      DataType datatype;
      if (ParseExpression(out datatype)) {
        var code = _emitter.GetSeg(marker);
        expr = new ExprInfo {
          Code = code,
          DataType = datatype,
        };
        Logger.WriteLine(3, "[OE expr {0}]", expr);
        ret = true;
      } else expr = new ExprInfo();
      return ret;
    }

    ///-----------------------------------------------------------------
    ///
    /// Type Parsing
    /// 

    //------------------------------------------------------------------
    // Parse a list of declarations (ident plus optional type)
    // decl_list := { ident [ : type ] SEP }
    bool ParseDeclList(List<Symbol> idents) {
      var idsym = Symbol.None;
      if (!(ParseDecl(out idsym))) return false;
      for (; ; ) {
        idsym.Kind = SymKinds.PARAM;
        if (idsym.DataType == DataTypes.Unknown) idsym.DataType = DataTypes.Text;
        idents.Add(idsym);
        if (!Match(Atoms.SEP)) return true;
        if (!(ParseDecl(out idsym))) return ErrExpect("identifier");
      }
    }

    //------------------------------------------------------------------
    // Parse a single ident and possible type
    // decl ::= ident [COLON type]
    bool ParseDecl(out Symbol idsym) {
      DataType datatype = DataTypes.Unknown;
      idsym = Look();
      if (!(idsym.IsDefinable)) return false;
      idsym = Take();
      if (Match(Atoms.COLON)) {
        // type is not optional
        if (!ParseType(out datatype)) return ErrExpect("type or expression");
        if (!datatype.IsVariable) return ErrSyntax("not a valid type");
        //if (ParseType(out datatype) && !datatype.IsVariable) return ErrSyntax("not a valid type");
      }
      if (idsym.Level != Scope.Current.Level)
        idsym = SymbolTable.MakeIdent(idsym.Name);
      idsym.DataType = datatype;
      return true;
    }

    //------------------------------------------------------------------
    // Connect Function ::= ident COLON db LP [ typelist ] RP
    // returns data type and emits code for runtime to make it so
    bool ParseConnect(out DataType datatype) {
      datatype = DataTypes.Unknown;
      if (!(Look().IsIdent && Look(1).Atom == Atoms.COLON && Look(2).Kind == SymKinds.DB)) return false;
      var idsym = Take();
      if (!idsym.IsDefinable) return ErrSyntax("already defined {0}", idsym.Name);
      Match(Atoms.COLON);
      var symbol = Take();

      if (!Match(Atoms.LP)) return ErrCheck(Atoms.LP);
      var source = TextValue.Default;
      if (Look().Kind == SymKinds.SOURCE) {
        source = Take().Value as TextValue;
        if (!(Match(Atoms.SEP) || Check(Atoms.RP))) return ErrCheck(Atoms.RP);
      }
      var datatypes = new List<DataType>();
      ParseTypeList(datatypes);
      if (!Match(Atoms.RP)) return ErrCheck(Atoms.RP);

      datatype = Catalog.GetRelvarType(idsym.Name, source.AsString());
      if (datatype == null) return ErrSyntax("not found: {0}", idsym.Name);

      idsym = SymbolTable.MakeCatVar(idsym.Name, datatype);
      Scope.Current.Add(idsym);
      SymbolTable.AddCatalog(idsym);

      _emitter.Out(Opcodes.LDVALUE, TextValue.Create(idsym.Name));
      _emitter.Out(Opcodes.LDVALUE, source);
      _emitter.Out(Opcodes.LDVALUE, HeadingValue.Create(datatype.Heading));
      _emitter.OutCall(symbol);
      //_emitter.OutCall(symbol, 0, symbol.CallInfo);
      return true;
    }

    //------------------------------------------------------------------
    // Udt ::= Id LC attr-list RC
    // Handles UDT selector functions
    bool ParseUserType(out DataType datatype) {
      datatype = DataTypes.Unknown;
      if (!(Look().Atom == Atoms.COLON && Look(1).IsIdent && Look(2).Atom == Atoms.LP)) return false;
      Match(Atoms.COLON);
      var idsym = Take();
      if (!idsym.IsDefinable) return ErrSyntax("already defined: {0}", idsym.Name);
      if (!Match(Atoms.LP)) return ErrCheck(Atoms.LP);
      Scope.Push();
      var args = new List<Symbol>();
      ParseDeclList(args);
      Scope.Pop();
      if (!Match(Atoms.RP)) return ErrCheck(Atoms.RP);

      var cols = MakeColumns(args);
      var udt = DataTypeUser.Get(idsym.Name, cols);
      idsym = SymbolTable.MakeUserType(idsym.Name, udt);
      Scope.Current.Add(idsym);
      SymbolTable.AddCatalog(idsym);
      return true;
    }

    //------------------------------------------------------------------
    // parse a list of types, identified by name or primary
    bool ParseTypeList(List<DataType> datatypes) {
      var datatype = DataTypes.Unknown;
      if (!ParseType(out datatype))
        return false;
      datatypes.Add(datatype);
      while (Match(Atoms.SEP)) {
        if (!ParseType(out datatype))
          return ErrExpect("type or typed value");
        datatypes.Add(datatype);
      }
      return true;
    }

    //------------------------------------------------------------------
    // Parse a type identified by name or primary
    bool ParseType(out DataType datatype) {
      datatype = null;
      var idsym = Look();
      if (idsym.IsIdent) {
        datatype = DataTypes.Find(idsym.Name) ?? DataTypeUser.Find(idsym.Name);
        if (datatype == null && idsym.IsUndefIdent)
          return false;
      }
      //if (idsym.IsIdent && DataTypes.Find(idsym.Name) != null)
      //  datatype = DataTypes.Find(idsym.Name);
      //else if (idsym.IsIdent && DataTypeUser.Find(idsym.Name) != null)
      //  datatype = DataTypeUser.Find(idsym.Name);
      if (datatype != null)
        Take();
      else {
        var marker = _emitter.GetMarker(); // FIX: just a symbol please
        if (!ParseSimplePrimary(out datatype))
          return false;
        _emitter.GetSeg(marker);
      }
      return true;
    }

    //------------------------------------------------------------------
    // Parse a definable identifier: IDENT or IDLIT
    bool ParseIdent(ref Symbol ident) {
      TakeEol();
      if (!Look().IsIdent)
        return false;
      ident = Take();
      return true;
    }

    ///=================================================================
    /// Helpers
    /// 

    // Wrap an expression in an aggregation -- only used by Fold
    void WrapAgg(ref ExprInfo expr, Symbol opsym) {
      var marker = _emitter.GetMarker();
      // create a seed to match the expression type
      var seed = opsym.GetSeed(expr.DataType);
      if (seed == null)
        ErrSyntax("aggregation not supported for {0}", expr.DataType);
      else _emitter.Out(Opcodes.LDAGG, seed);
      _emitter.Out(expr.Code);
      DataType datatype;
      CallInfo callinfo;
      CheckTypeError(opsym, out datatype, out callinfo, expr.DataType, expr.DataType); // as if there were two
      _emitter.OutCall(opsym, 0, callinfo);
      expr.DataType = datatype;
      expr.Code = _emitter.GetSeg(marker);
    }

    ///=================================================================
    /// Type checking
    /// 

    // calculate return type given a method and arguments
    // returns true if type error detected 
    bool CheckTypeError(Symbol symbol, out DataType datatype, out CallInfo callinfo, params DataType[] datatypes) {
      datatype = symbol.DataType;
      callinfo = symbol.CallInfo;
      var nargs = symbol.NumArgs; // how many to check
      if (!(datatypes.Length == nargs))
        return ErrSyntax("'{0}' expected {1} arguments, found {2}", symbol.Name, nargs, datatypes.Length);
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
        return ErrSyntax("'{0}' type mismatch", symbol.Name);
      if (symbol.IsDyadic)
        if (CheckDyadicType(symbol.MergeOp, datatypes[0], datatypes[1], ref datatype))
          return true;
      if (datatype == DataTypes.Table && nargs >= 1 && datatypes[0] is DataTypeRelation)
        datatype = datatypes[0];
      Logger.Assert(datatype.Flags.HasFlag(TypeFlags.Variable) || datatype == DataTypes.Unknown || datatype == DataTypes.Void, datatype.Name);
      return false;
    }

    bool CheckDyadicType(MergeOps mops, DataType reltype1, DataType reltype2, ref DataType datatype) {
      if (!(reltype1 is DataTypeRelation && reltype2 is DataTypeRelation))
        return ErrSyntax("relational arguments expected");
      var cols = DataColumn.Merge(mops, reltype1.Heading.Columns, reltype2.Heading.Columns);
      var dups = cols.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
      if (dups.Length > 0)
        return ErrSyntax("duplicate attribute: {0}", String.Join(",", dups));
      datatype = DataTypeRelation.Get(DataHeading.Create(cols));
      return false;
    }

    // Make an expression block that projects to match a heading
    ExpressionBlock[] MakeProject(DataHeading heading) {
      return heading.Columns
        .Select(c => ExpressionBlock.Create(c.Name, c.Name, c.DataType)).ToArray();
    }

    public static DataColumn[] MakeColumns(IEnumerable<Symbol> syms) {
      return syms.Select(s => DataColumn.Create(s.Name, s.DataType)).ToArray();
    }

    // resolve return type given a method and a list of types
    // requires calls to runtime functions
    // returns true if type error detected 
    // DEAD:
    bool ResolveTypes(Symbol symbol, out DataType datatype, out CallInfo callinfo, params DataType[] datatypes) {
      datatype = symbol.DataType;
      callinfo = symbol.CallInfo;
      string name = symbol.Name;
      if (symbol.Kind == SymKinds.DB) {
        datatype = Catalog.GetRelvarType(name, "");
        return datatype != null || ErrSyntax("not found: {0}", name);
      }
      return ErrSyntax("unknown function {0}", name);
    }

    ///=================================================================
    /// Tokens and errors
    /// 

    Symbol Look(int n = 0) {
      return _lexer.LookAhead(n);
    }

    Symbol LookOverEol(int n = 0) {
      while (_lexer.LookAhead(n).Atom == Atoms.EOL)
        ++n;
      return _lexer.LookAhead(n);
    }

    Symbol Take() {
      var ret = Look();
      _lexer.Next();
      return ret;
    }

    void Untake() {
      _lexer.Back();
    }

    bool Check(Atoms atom) {
      return atom == Look().Atom;
    }

    bool Match() {
      _lexer.Next();
      return true;
    }

    bool MatchOverEol(Atoms atom) {
      if (atom != LookOverEol().Atom) return false;
      TakeEol();
      return Match(atom);
    }

    bool Match(Atoms atom) {
      // tokens that are allowed to come AFTER EOL
      if (atom == Atoms.RP || atom == Atoms.RB || atom == Atoms.RC || atom == Atoms.EOF)
        TakeEol();
      if (atom != Look().Atom) return false;
      _lexer.Next();
      // tokens that are allowed to come BEFORE EOL
      if (atom == Atoms.LP || atom == Atoms.LB || atom == Atoms.LC || atom == Atoms.SEP)
        TakeEol();
      return true;
    }

    void TakeEol() {
      while (Match(Atoms.EOL))
        ;
    }

    //--- error functions return true if error

    bool ErrSyntax(string message, params object[] args) {
      Logger.WriteLine("Error line {0}: {1}", _lexer.LineNumber, String.Format(message, args));
      Error = true;
      return true;
    }

    bool ErrCheck(bool test, string message, params object[] args) {
      if (test) return false;
      return ErrSyntax(message, args);
    }

    bool ErrCheck(Atoms atom) {
      if (Look().Atom == atom) return false;
      return ErrExpect(atom.ToString());    // TODO: better names for atoms
    }

    bool ErrExpect(string name) {
      return ErrSyntax("expected {0}, found {1}", name, Look().Name);
    }

    bool ErrExpect(Atoms atom) {
      return ErrSyntax("expected {0}, found {1}", atom.ToString(), Look().Name);
    }

    bool ErrNotExpect(Atoms atom) {
      return ErrSyntax("found unexpected {0}", atom.ToString());
    }

    bool ErrType(int arg, DataType exp, DataType found) {
      return ErrSyntax("type mismatch for argument {0}, expected {1} but found {2}", arg, exp, found);
    }

  }
}
