using System;
using System.Collections.Generic;
using System.Linq;
using Pegasus.Common;
using System.IO;
using Andl.Runtime;
using Andl.Common;

namespace Andl.Peg {
  /// <summary>
  /// Compile input according to PEG grammar
  /// Execute incrementally unless suppressed or error
  /// </summary>
  public class PegCompiler : IParser {
    // get whether error happened on this statement
    public bool Error { get; private set; }
    // get or set debug level
    public int ErrorCount { get; private set; }
    // true if compilation was aborted
    public bool Aborted { get; set; }
    // get instance of symbol table
    public SymbolTable Symbols { get; private set; }
    // get set instance of Catalog
    public Catalog Cat { get; private set; }

    // Create a parser with catalog and symbols
    public static IParser Create(Catalog catalog) {
      return new PegCompiler {
        Symbols = SymbolTable.Create(catalog),
        Cat = catalog,
      };
    }

    // Compile from input, write to output
    // Evaluate incrementally
    // Note: compiler will load symbols from catalog after processing initial whitespace,
    // including directives if any.
    public bool RunScript(TextReader input, TextWriter output, Evaluator evaluator, string filename) {
      Logger.WriteLine(2, $">Parser '{filename}'");
        var parser = new PegParser {
        Symbols = Symbols, Output = output, Cat = Cat
      };
      Error = false;
      ErrorCount = 0;
      Aborted = false;
      bool done = false;
      var exec = Cat.ExecuteFlag;

      parser.LoadSource(input, filename);
      Cursor state = null;
      while (!done) {
        try {
          var result = (state == null) ? parser.Parse(parser.InputText) : parser.Restart(ref state);
          if (result is AstEof) {
            if (parser.TryPopState())
              state = parser.State;
            else done = true;
          } else if (result is AstEmpty) {
            parser.TryPushState(); // maybe changed because of #include
            state = parser.State;
          } else {
            var code = Emit(result);
            if (Logger.Level >= 3)
              Decoder.Create(code).Decode();
            if (exec && ErrorCount == 0)
              Execute(evaluator, code);
            state = parser.State;
          }
        } catch (ParseException ex) {
           state = ex.State;
          if (ex.Stop) done = true;
          else ErrorCount++;
        } catch (Exception ex) {
          output.WriteLine(ex.ToString());
          //output.WriteLine(ex.Data["state"]);
          ErrorCount++;
          Aborted = true;
          break;
        }
      }

      var ok = done && ErrorCount == 0 && !Aborted;
      Cat.EndSession(ok ? SessionResults.Ok : SessionResults.Failed);
      return ok;
    }

    ///============================================================================================
    ///
    /// Emit and execute
    ///

    ByteCode Emit(AstStatement statement) {
      Logger.Assert(statement.DataType != null);
      Logger.WriteLine(4, "|{0}", statement);  // nopad
      var emitter = new Emitter();
      statement.Emit(emitter);
      if (statement.DataType.IsVariable) {
      //if (statement.DataType != DataTypes.Void) {
        emitter.OutCall(Symbols.FindIdent("pp"));
        emitter.OutCall(Symbols.FindIdent("write"));
      }
      // statement will have left value on stack (even if void)
      if (!(statement is AstDefine)) emitter.Out(Opcodes.EOS);
      var code = emitter.GetCode();
      return code;
    }

    void Execute(Evaluator evaluator, ByteCode code) {
      Logger.WriteLine(3, "Execute: len:{0}", code.Length);
      try {
        evaluator.Exec(code);
      } catch (ProgramException ex) {
        Logger.WriteLine("Program error: {0}", ex.ToString());
        ++ErrorCount;
      }
      Logger.WriteLine(3, "[Ex {0}]", ErrorCount);
    }
  }
}
