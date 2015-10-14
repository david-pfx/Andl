using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using Thrift;
using Thrift.Server;
using Thrift.Transport;
using Thrift.Protocol;
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
        //TServer server = new TThreadPoolServer(processor, serverTransport, new TTransportFactory(), new TCompactProtocol.Factory());
        TServer server = new TThreadPoolServer(processor, serverTransport);
        //TServer server = new TThreadedServer(processor, serverTransport);
        //TServer server = new TSimpleServer(processor, serverTransport);
        server.setEventHandler(new ServerEventHandler());
        Console.WriteLine("Starting the server...default database");
        server.Serve();
      } catch (Exception ex) {
        Console.WriteLine(ex.StackTrace);
      }
      Console.WriteLine("done.");
    }

    static HashSet<string> _settingshash = new HashSet<string> {
      "DatabasePath", "DatabaseSqlFlag", "DatabaseName", "Noisy"
    };

    static Gateway AppStartup() {
      var appsettings = ConfigurationManager.AppSettings;
      // Convert dictionary type, filter out unwanted
      var settings = appsettings.AllKeys
        .Where(k => _settingshash.Contains(k))
        .ToDictionary(k => k, k => appsettings[k]);
      return Andl.API.Gateway.StartUp(settings);
    }

    /// <summary>
    /// Implement server event handler
    /// </summary>
    class ServerEventHandler : TServerEventHandler {

      public object createContext(global::Thrift.Protocol.TProtocol input, global::Thrift.Protocol.TProtocol output) {
        Console.WriteLine("Connected... {0} {1}", input, output);
        return null;
      }

      public void deleteContext(object serverContext, global::Thrift.Protocol.TProtocol input, global::Thrift.Protocol.TProtocol output) {
      }

      public void preServe() {
      }

      public void processContext(object serverContext, TTransport transport) {
      }
    }

  }
}
