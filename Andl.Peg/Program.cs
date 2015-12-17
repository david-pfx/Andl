using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pegasus.Common;

namespace Andl.Peg {
  class Program {
    static void Main(string[] args) {
      var types = new TypeSystem();
      var syms = new SymbolTable();
      var parser = new PegParser {
        AST = new AstFactory { Types = types, Syms = syms },
        Types = types,
      };
      var inpath = "test-50.andl";
      var input = new StreamReader(inpath);
      var intext = input.ReadToEnd();
      Cursor state = null;
      for (var done = false; !done;) {
        try {
          var result = (state == null) ? parser.Parse(intext) : parser.Restart(ref state);
          Console.WriteLine("Complete: {0} errors.", parser.ErrorCount);
          Console.WriteLine(result);
          done = true;
        } catch (ParseException ex) {
          state = ex.State;
        } catch (Exception ex) {
          Console.WriteLine(ex.ToString());
          Console.WriteLine(ex.Data["state"]);
          done = true;
        }
      }
    }
  }

  
}
