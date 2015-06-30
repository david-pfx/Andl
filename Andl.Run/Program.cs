using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Andl.API;

namespace Andl.Run {
  class Program {
    static void Main(string[] args) {
      var settings = new Dictionary<string, string> {
        { "DatabasePath", @"D:\MyDocs\Dev\vs13\Andl\Work\andltest.sandl" },
      };

      var app = Andl.API.Runtime.StartUp(settings);

      string[] names = { "suppliers", "parts", "supplies", "sandsp" };
      foreach (var n in names) {
        var x = app.GetValue(n);
        Console.WriteLine("{0}: {1} {2} {3}", n, x.Ok, x.Message, x.Ok ? x.Value.ToString() : "??");
      }
    }
  }
}
