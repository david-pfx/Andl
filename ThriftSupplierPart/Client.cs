using System;
using System.Collections.Generic;
using Thrift.Protocol;
using Thrift.Transport;

namespace ThriftSupplierPart {
  class SupplierPartClient {
    static void Main(string[] args) {
      try {
        TTransport transport = new TSocket("localhost", 9095);
        TProtocol protocol = new TBinaryProtocol(transport);
        ThriftSupplierPartService.Client client = new ThriftSupplierPartService.Client(protocol);
        Console.WriteLine("Open transport.");
        transport.Open();
        Console.WriteLine("Begin tests.");
        try {
          Console.WriteLine("find_all");
          var ss = client.findall_supplier();
          foreach (var s in ss)
            Console.WriteLine(" {0}", s);

          var sid = "S3";
          Console.WriteLine("find {0}", sid);
          ss = client.find_supplier(sid);
          foreach (var s in ss)
            Console.WriteLine(" {0}", s);

          sid = "S99";
          Console.WriteLine("find {0}", sid);
          ss = client.find_supplier(sid);
          foreach (var s in ss)
            Console.WriteLine(" {0}", s);

          var sn = new List<Supplier> {
            new Supplier { Sid = "S99", SNAME = "Dracula", STATUS = -1, CITY = "Transylvania" }
          };
          Console.WriteLine("add {0}", String.Join(",", sn));

          client.create_supplier(sn);

          Console.WriteLine("find_all");
          ss = client.findall_supplier();
          foreach (var s in ss)
            Console.WriteLine(" {0}", s);

          sid = "S99";
          Console.WriteLine("find {0}", sid);
          ss = client.find_supplier(sid);
          foreach (var s in ss)
            Console.WriteLine(" {0}", s);

          //=== part

          Console.WriteLine("find_all_part");
          var pp = client.findall_part();
          foreach (var p in pp)
            Console.WriteLine(" {0}", p);

          var kv = new List<Tquery> {
            new Tquery { Key = "PNAME", Value = "C" },
          };
          Console.WriteLine("find_some_part {0}", kv[0]);
          pp = client.findsome_part(kv);
          foreach (var p in pp)
            Console.WriteLine(" {0}", p);

          //=== supplies

          Console.WriteLine("find_all_supplies");
          var uu = client.findall_supplies();
          foreach (var u in uu)
            Console.WriteLine(" {0}", u);

          Console.WriteLine("Tests completed.");
        } finally {
          transport.Close();
          Console.WriteLine("Transport closed.");
        }
      } catch (Exception x) {
        Console.WriteLine(x.ToString());
      }

    }
  }
}
