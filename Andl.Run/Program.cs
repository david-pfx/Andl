using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Andl.Gateway;

namespace Andl.Run {
  class Program {
    static void Main(string[] args) {
      var settings = new Dictionary<string, string> {
        //{ "DatabasePath", @"D:\MyDocs\Dev\vs13\Andl\Work\andltest.sandl" },
      };

      var app = Andl.Gateway.GatewayFactory.Create("data", settings);

      string[] names = { "S", "P", "SP" };
      foreach (var n in names) {
        var x = app.GetValue(n);
        Console.WriteLine("{0}: {1} {2} {3}", n, x.Ok, x.Message, x.Ok ? Decode(x.Value) : "??");
      }

      var cat = app.GetValue("andl_catalog");
      var v = cat.Value;

    }

    static string Decode(object value) {
      var type = value.GetType();
      var countpi = type.GetProperty("Count");
      var itempi = type.GetProperty("Item");
      var count = (int)countpi.GetValue(value, null);

      var ret = new List<object>();
      for (var x = 0; x < count; ++x) {
        var y = itempi.GetValue(value, new object[] { x });
        ret.Add(y);
      }
      return ret.ToString();
    }
  }
}
