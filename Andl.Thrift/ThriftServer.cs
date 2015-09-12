using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using Thrift;
using Thrift.Server;
using Thrift.Transport;
using Andl.API;
using Andl.Runtime; // for logging!

namespace Andl.Thrift {
  /// <summary>
  /// Implement Thrift server
  /// </summary>
  class ThriftServer {
    static void Main(string[] args) {
      Console.WriteLine("Andl Thrift Server");
      Logger.Open(0);   // no default logging
      try {
        var gateway = AppStartup();
        Processor processor = new Processor(gateway);
        TServerTransport serverTransport = new TServerSocket(9095); // TODO: config it
        TServer server = new TSimpleServer(processor, serverTransport);
        Console.WriteLine("Starting the server...");
        server.Serve();
      } catch (Exception ex) {
        Console.WriteLine(ex.StackTrace);
      }
      Console.WriteLine("done.");
    }

    static HashSet<string> _settingshash = new HashSet<string> {
      "DatabasePath", "DatabaseSqlFlag", "CatalogName", "Noisy"
    };

    static Gateway AppStartup() {
      var appsettings = ConfigurationManager.AppSettings;
      // Convert dictionary type, filter out unwanted
      var settings = appsettings.AllKeys
        .Where(k => _settingshash.Contains(k))
        .ToDictionary(k => k, k => appsettings[k]);
      return Andl.API.Gateway.StartUp(settings);
    }

  }
}
