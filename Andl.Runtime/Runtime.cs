using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Andl.Runtime;

namespace Andl {
  public interface IParser {
    bool Process(TextReader input, TextWriter output, Evaluator evaluator, string filename);
    int ErrorCount { get; }
  }
}

namespace Andl.Runtime {
  public class Runtime {
  }
}
