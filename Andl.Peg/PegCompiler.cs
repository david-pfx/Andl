using System;
using System.Collections.Generic;
using System.Linq;
using Pegasus.Common;
using System.IO;
using Andl.Runtime;

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
    public Catalog Catalog { get; private set; }

    // Create a parser with catalog and symbols
    public static IParser Create(Catalog catalog) {
      return new PegCompiler {
        Symbols = SymbolTable.Create(catalog),
        Catalog = catalog,
      };
    }

    // Compile from input, write to output
    // Evaluate incrementally
    public bool Process(TextReader input, TextWriter output, Evaluator evaluator, string filename) {
      var parser = new PegParser {
        Symbols = Symbols, Output = output, Cat = Catalog
      };
      Error = false;
      ErrorCount = 0;
      Aborted = false;

      //var intext = input.ReadToEnd();
      var exec = Catalog.ExecuteFlag;
      parser.Start(input, filename);
      Cursor state = null;
      while (!parser.Done) { // also set by #stop
        try {
          var result = (state == null) ? parser.Parse(parser.InputText) : parser.Restart(ref state);
          if (result == null) // EOF
            parser.Done = true;
          else {
            var code = Emit(result);
            if (Logger.Level >= 3)
              Decoder.Create(code).Decode();
            if (exec && parser.ErrorCount == 0)
              Execute(evaluator, code);
            //allcode.Add(code.bytes);
            state = parser.State;
          }
        } catch (ParseException ex) {
          state = ex.State;
        } catch (Exception ex) {
          output.WriteLine(ex.ToString());
          //output.WriteLine(ex.Data["state"]);
          ErrorCount++;
          Aborted = true;
          break;
        }
      }
      ErrorCount += parser.ErrorCount;
      //if (parser.Done && ErrorCount == 0) {
      //  if (Logger.Level >= 4)
      //    Decoder.Create(allcode).Decode();
      //  if (Catalog.ExecuteFlag) {
      //    output.WriteLine("*** Begin execution");
      //    Execute(evaluator, allcode);
      //  }
      //  return true;
      //}
      return parser.Done && ErrorCount == 0 && !Aborted;
    }

    ///============================================================================================
    ///
    /// Emit and execute
    ///

    ByteCode Emit(AstStatement statement) {
      Logger.Assert(statement.DataType != null);
      Logger.WriteLine(4, ">{0}", statement);
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
      try {
        evaluator.Exec(code);
      } catch (ProgramException ex) {
        Logger.WriteLine(ex.ToString());
      }
    }
  }
}
