using System;
using System.Collections.Generic;
using System.Linq;
using System.Configuration;
using Thrift;
using Thrift.Server;
using Thrift.Transport;
using Thrift.Protocol;
using Andl.Gateway;
using System.Text.RegularExpressions;
using Andl.Common;

namespace Andl.Thrift {
  /// <summary>
  /// Implement Thrift server
  /// 
  /// This is entirely generic, with nothing application-specific here. 
  /// That is all handled by the Andl code, previously compiled into the database.
  /// </summary>
  class ThriftServer {
    const int ServerPort = 9095;  // TODO: config it
    const string Help = "Andl.Thrift [<database name or path>] [/options]\n"
      + "\t\tDefault database is 'db', path is 'db.sandl' or 'db.sqandl' for sqlite.\n"
      + "\t/s\tSql database\n"
      + "\t/o\tOnce only, exit on first disconnect\n"
      + "\t/n\tn=1 to 4, set tracing level";
    static readonly Dictionary<string, Action<string>> _options = new Dictionary<string, Action<string>> {
      { "s", (a) => _settings["Sql"] = "true" },
      { "o", (a) => Once = true },
    };
    static Dictionary<string, string> _settings = new Dictionary<string, string>();
    static bool Once { get; set; }
    static TServer Server { get; set; }

    // main entry creates and launches the server
    static void Main(string[] args) {
      Console.WriteLine("Andl Thrift Server");
      Logger.Open(0);   // no default logging
      var options = OptionParser.Create(_options, Help);
      if (!options.Parse(args))
        return;
      GatewayBase gateway = null;
      try {
        gateway = AppStartup(options.GetPath(0), _settings);
        Processor processor = new Processor(gateway);
        TServerTransport serverTransport = new TServerSocket(ServerPort); 

        // There are several different servers available. They should all work.

        Server = new TThreadPoolServer(processor, serverTransport);
        //TServer server = new TThreadPoolServer(processor, serverTransport);
        //TServer server = new TThreadPoolServer(processor, serverTransport, new TTransportFactory(), new TCompactProtocol.Factory());
        //TServer server = new TThreadedServer(processor, serverTransport);
        //TServer server = new TSimpleServer(processor, serverTransport);

        Server.setEventHandler(new ServerEventHandler());
        gateway.OpenSession();
        Console.WriteLine($"Starting server...database '{gateway.DatabaseName}' {gateway.DatabaseKind}");
        Server.Serve();
        gateway.CloseSession(true);
      } catch (ProgramException ex) {
        Console.WriteLine(ex.ToString());
        if (gateway != null) gateway.CloseSession(false);
      } catch (Exception ex) {
        Console.WriteLine(ex.ToString());
        if (gateway != null) gateway.CloseSession(false);
      }
      Console.WriteLine("done.");
    }

    static GatewayBase AppStartup(string database, Dictionary<string,string> settings) {
      //var appsettings = ConfigurationManager.AppSettings;
      return GatewayFactory.Create(database, settings);
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
        Console.WriteLine("Disconnected... {0} {1}", input, output);
        if (Once) Server.Stop();
      }

      public void preServe() {
      }

      public void processContext(object serverContext, TTransport transport) {
      }
    }

  }
}
