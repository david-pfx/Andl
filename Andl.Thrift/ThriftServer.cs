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
using Andl.Gateway;
using Andl.Runtime; // for logging!
using System.Text.RegularExpressions;

namespace Andl.Thrift {
  /// <summary>
  /// Implement Thrift server
  /// 
  /// This is entirely generic, with nothing application-specific here. 
  /// That is all handled by the Andl code.
  /// </summary>
  class ThriftServer {
    const int ServerPort = 9095;  // TODO: config it

    // main entry creates and launches the server
    static void Main(string[] args) {
      Console.WriteLine("Andl Thrift Server");
      Logger.Open(0);   // no default logging
      var options = new OptionParser();
      if (!options.Parse(args))
        return;
      try {
        var gateway = AppStartup(options.GetPath(0));
        Processor processor = new Processor(gateway);
        TServerTransport serverTransport = new TServerSocket(ServerPort); 

        // There are several different servers available. They should all work.

        TServer server = new TThreadPoolServer(processor, serverTransport);
        //TServer server = new TThreadPoolServer(processor, serverTransport, new TTransportFactory(), new TCompactProtocol.Factory());
        //TServer server = new TThreadedServer(processor, serverTransport);
        //TServer server = new TSimpleServer(processor, serverTransport);

        server.setEventHandler(new ServerEventHandler());
        Console.WriteLine("Starting server...database '{0}'", gateway.DatabaseName);
        server.Serve();
      } catch (ProgramException ex) {
        Console.WriteLine(ex.ToString());
      } catch (Exception ex) {
        Console.WriteLine(ex.ToString());
      }
      Console.WriteLine("done.");
    }

    static HashSet<string> _settingshash = new HashSet<string> {
      //"DatabasePath", "DatabaseSqlFlag", "DatabaseName", "Noisy"
    };

    static GatewayBase AppStartup(string database = null) {
      var appsettings = ConfigurationManager.AppSettings;
      // Convert dictionary type, filter out unwanted
      var settings = appsettings.AllKeys
        .Where(k => _settingshash.Contains(k))
        .ToDictionary(k => k, k => appsettings[k]);
      return GatewayFactory.Create(database, settings);
    }

    /// <summary>
    /// Parse some options and filenames
    /// </summary>
    class OptionParser {
      internal string GetPath(int n) {
        return (n < _paths.Count) ? _paths[n] : null;
      }
      internal bool GetSwitch(string name) {
        return _switches.ContainsKey(name) ? _switches[name] : false;
      }

      List<string> _paths = new List<string>();
      Dictionary<string, bool> _switches = new Dictionary<string, bool>();
      const string _help = "Andl.Thrift [<database name or path>] options\n"
        + "\t\tDefault database is 'data', path is 'data.sandl' or 'data.sqandl' for sql.\n"
        + "\t/n\tn=1 to 4, show logging\n";

      internal bool Parse(string[] args) {
        for (var i = 0; i < args.Length; ++i) {
          if (args[i].StartsWith("/") || args[i].StartsWith("-")) {
            if (!Option(args[i].Substring(1))) {
              return false;
            }
          } else _paths.Add(args[i]);
        }
        return true;
      }

      // Capture the options
      bool Option(string arg) {
        if (arg == "?") {
          Logger.WriteLine(_help);
          return false;
        } else if (Regex.IsMatch(arg, "[0-9]+"))
          Logger.Level = int.Parse(arg);
        else {
          Logger.WriteLine("*** Bad option: {0}", arg);
          return false;
        }
        return true;
      }

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
