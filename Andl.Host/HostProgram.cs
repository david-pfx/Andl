using System;
using System.Collections.Generic;
using System.Text;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.ServiceModel.Description;
using System.IO;
using System.Net;
using System.ServiceModel.Channels;
using System.Linq;
using System.Configuration;
using Andl.Gateway;
using Andl.Common;

namespace Andl.Host {
  ///===========================================================================
  /// <summary>
  /// Implement both server and client, using WCF.
  /// 
  /// First run an Andl script to create a database containing callable 
  /// functions and data, which are stored in the catalog. 
  /// The default database 'db' should be used for testing.
  /// 
  /// Then run this. 
  /// 1. Starts up Andl via its gateway
  /// 2. Creates a WebServiceHost
  /// 3. Sends through a series of tests addressed to database 'db'.
  /// 4. Waits for console input.
  /// </summary>
  class HostProgram {
    const int ServerPort = 8000;  // TODO: config it
    const string Help = "Andl.Host [<database name or path>] [/options]\n"
      + "\t\tDefault database is 'db', path is 'db.sandl' or 'db.sqandl' for sqlite.\n"
      + "\t/s\tSql database\n"
      + "\t/n\tNo tests, just run as server\n"
      + "\t/n\tn=1 to 4, set tracing level";
    static readonly Dictionary<string, Action<string>> _options = new Dictionary<string, Action<string>> {
      { "s", (a) => _settings["Sql"] = "true" },
      { "n", (a) => NoTests = true },
    };
    static Dictionary<string, string> _settings = new Dictionary<string, string>();
    static bool NoTests { get; set; }

    static GatewayBase _gateway;
    // return gateway for this catalog, used by server request handler
    public static GatewayBase GetGateway(string catalog) {
      return catalog == _gateway.DatabaseName ? _gateway : null;
    }

    //-------------------------------------------------------------------------
    // mainline
    // start up web host, then run tests
    static void Main(string[] args) {
      Logger.Open(0);   // no default logging
      Logger.WriteLine("Andl.Host");
      var options = OptionParser.Create(_options, Help);
      if (!options.Parse(args))
        return;
      try {
        AppStartup(options.GetPath(0), _settings);
        _gateway.OpenSession();
        string address = $"http://{Environment.MachineName}:{ServerPort}/api";
        Logger.WriteLine(1, $"Opening host with database: {_gateway.DatabaseName} ({_gateway.DatabaseKind}) address: {address}");
        var host = CreateHost(address, "");
        host.Open();
        Logger.WriteLine(1, "Host opened.");
        if (!NoTests) SendTests(address);
        Console.WriteLine("Press Enter to close.");
        Console.ReadLine();
        host.Close();
        _gateway.CloseSession();
      } catch (ProgramException ex) {
        Console.WriteLine(ex.ToString());
        if (_gateway != null) _gateway.CloseSession(false);
      } catch (Exception ex) {
        Console.WriteLine(ex.ToString());
        if (_gateway != null) _gateway.CloseSession(false);
      }
    } 

    static WebServiceHost CreateHost(string address, string endpoint) {
      var host = new WebServiceHost(typeof(RawDataService), new Uri(address));
      host.AddServiceEndpoint(typeof(IRestContract), GetBinding(), endpoint);
      ServiceDebugBehavior sdb = host.Description.Behaviors.Find<ServiceDebugBehavior>();
      sdb.HttpHelpPageEnabled = false;
      return host;
    }

    // Force content mapping to use Raw always (otherwise it traps any Json or XML).
    // see http://blogs.msdn.com/b/carlosfigueira/archive/2008/04/17/wcf-raw-programming-model-receiving-arbitrary-data.aspx
    public class RawMapper : WebContentTypeMapper {
      public override WebContentFormat GetMessageFormatForContentType(string contentType) {
        if (contentType == "application/json") return WebContentFormat.Raw;
        else return WebContentFormat.Default;
      }
    }

    static Binding GetBinding() {
      CustomBinding result = new CustomBinding(new WebHttpBinding());
      var wmebe = result.Elements.Find<WebMessageEncodingBindingElement>();
      wmebe.ContentTypeMapper = new RawMapper();
      return result;
    }

    //-------------------------------------------------------------------------
    // App startup
    static void AppStartup(string database, Dictionary<string, string> settings) {
      //can use App.Config instead
      //var appsettings = ConfigurationManager.AppSettings;
      //var settings = appsettings.AllKeys
      //  .ToDictionary(k => _settingsdict[k], k => appsettings[k]);
      _gateway = Andl.Gateway.GatewayFactory.Create(database, settings);
      _gateway.JsonReturnFlag = true;   // FIX: s/b default
    }

    //-------------------------------------------------------------------------
    //--- test only

    // Note: some will trigger error response and raise an exception

    static void SendTests(string baseaddress) {
      SendRequest(baseaddress + "/db/supplier", "GET");
      SendRequest(baseaddress + "/db/repl", "POST", "text", "S join SP");
      SendRequest(baseaddress + "/db/repl", "POST", "json", "'S join SP'");
      SendRequest(baseaddress + "/db/repl", "POST", "text", "S join SP");
      SendRequest(baseaddress + "/db/repl", "POST", "json", "'S join SP'");
      SendRequest(baseaddress + "/db/supplier/S2", "GET");
      var newsupp = "[{'Sid':'S9','SNAME':'Adolph','STATUS':99,'CITY':'Melbourne'}]".Replace('\'', '"');
      SendRequest(baseaddress + "/db/supplier/x", "POST", "json", newsupp);
//#if tests
      SendRequest(baseaddress + "/db/supplier/", "POST", "json", newsupp);
      SendRequest(baseaddress + "/db/supplier", "POST", "json", newsupp);
      SendRequest(baseaddress + "/db/supplier", "DELETE");
      SendRequest(baseaddress + "/db/supplier/S9", "DELETE");
      SendRequest(baseaddress + "/db/supplier/S9", "GET");
      SendRequest(baseaddress + "/db/supplier", "PUT", "json", newsupp);
      SendRequest(baseaddress + "/db/supplier/S9", "PUT", "json", newsupp);
      SendRequest(baseaddress + "/db/supplier/S9", "GET");
      SendRequest(baseaddress + "/db/part", "GET");
      SendRequest(baseaddress + "/db/part?PNAME=S.*", "GET");
      SendRequest(baseaddress + "/db/xsupplier", "DELETE");
      SendRequest(baseaddress + "/db/badsupplier", "GET");
      SendRequest(baseaddress + "/db/badsupplier", "POST", "json", "post 1");
//#endif
    }

    static void SendRequest(string address, string verb, string kind = null, string content = null) {
      Logger.WriteLine(1, "Client: Send {0} {1} {2}", address, verb, kind);
      HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(address);
      req.Method = verb;

      if (kind != null) {
        Stream reqStream = req.GetRequestStream();
        var bytes = Encoding.UTF8.GetBytes(content);
        reqStream.Write(bytes, 0, bytes.Length);
        reqStream.Close();
        req.ContentType = (kind == "json") ? "application/json" : "plain/text";
      }
      //--- send it
      HttpWebResponse resp;
      try {
        resp = (HttpWebResponse)req.GetResponse();
      } catch (WebException e) {
        resp = e.Response as HttpWebResponse;
      }

      Logger.WriteLine(1, "Client: Receive Response HTTP/{0} {1} {2} type {3} length {4}",
        resp.ProtocolVersion, (int)resp.StatusCode, resp.StatusDescription, resp.ContentType, resp.ContentLength);
      var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8);
      var body = sr.ReadToEnd();
      Logger.WriteLine(1, "Body: <{0}>\n", body);
      resp.Close();
    }
  }

  ///===========================================================================
  /// <summary>
  /// Defined entry points for server
  /// </summary>
  [ServiceContract]
  public interface IRestContract {
    [WebInvoke(Method = "POST", UriTemplate = "{catalog}/repl")]
    Stream DoRepl(string catalog, Stream body);

    [WebInvoke(Method = "GET", UriTemplate = "{catalog}/{name}/{id=null}")]
    Stream DoGet(string catalog, string name, string id);

    [WebInvoke(Method = "PUT", UriTemplate = "{catalog}/{name}/{id=null}",
      ResponseFormat = WebMessageFormat.Json)]
    Stream DoPut(string catalog, string name, string id, Stream body);

    [WebInvoke(UriTemplate = "{catalog}/{name}/{id=null}")]
    Stream DoPost(string catalog, string name, string id, Stream body);

    [WebInvoke(Method = "DELETE", UriTemplate = "{catalog}/{name}/{id=null}")]
    Stream DoDelete(string catalog, string name, string id);

  }

  ///===========================================================================
  /// <summary>
  /// Implementation of defined service
  /// </summary>
  public class RawDataService : IRestContract {

    //-- interface functions
    public Stream DoGet(string catalog, string name, string id) {
      return Common("Get", catalog, name, id);
    }

    public Stream DoPut(string catalog, string name, string id, Stream body) {
      return Common("Put", catalog, name, id, body);
    }

    public Stream DoPost(string catalog, string name, string id, Stream body) {
      return Common("Post", catalog, name, id, body);
    }

    public Stream DoDelete(string catalog, string name, string id) {
      return Common("Delete", catalog, name, id);
    }

    public Stream DoRepl(string catalog, Stream body) {
      return Exec(catalog, body);
    }

    // common code for all requests
    Stream Common(string method, string catalog, string name, string id, Stream body = null) {
      string content = (body == null) ? null : ReadStream(body);
      var qparams = GetQueryParams();
      Logger.WriteLine(2, "Server {0}: {1},{2},{3},{4} <{5}>", method, catalog, name, id, KvpToString(qparams), content);

      // could be extended to support multiple databases
      var gateway = HostProgram.GetGateway(catalog);
      var result = (gateway == null) ? Result.Failure("catalog not found: " + catalog)
        : gateway.JsonCall(method, name, id, qparams, content);

      if (result.Ok) { 
        // string returned will actually be prefab json.
        WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.OK;
        WebOperationContext.Current.OutgoingResponse.ContentType = "application/json";
        return ToStream(result.Value as string);
      } else {
        // Bad result should cause bad request, but then it sends html. Maybe later?
        WebOperationContext.Current.OutgoingResponse.StatusCode = HttpStatusCode.OK;
        WebOperationContext.Current.OutgoingResponse.ContentType = "text/plain";
        return ToStream(result.Message);
      }
      //WebOperationContext.Current.OutgoingResponse.StatusCode = result.Ok ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
      //WebOperationContext.Current.OutgoingResponse.ContentType = "application/json";
      //return ToStream(result.Ok ? result.Value as string : result.Message);
    }

    // handle repl execution
    // gateway responsible for decoding raw/json input and providing json output
    Stream Exec(string catalog, Stream body) {
      string content = ReadStream(body);
      Logger.WriteLine(2, "Server {0}: {1} <{2}>", "repl", catalog, content);

      var mode = (WebOperationContext.Current.IncomingRequest.ContentType == "application/json") ? ExecModes.JsonString : ExecModes.Raw;
      var gateway = HostProgram.GetGateway(catalog);
      var result = gateway.RunScript(content, mode);

      WebOperationContext.Current.OutgoingResponse.ContentType = (mode == ExecModes.Raw || !result.Ok) ? "text/plain" : "application/json";
      WebOperationContext.Current.OutgoingResponse.StatusCode = result.Ok ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
      return ToStream(result.Ok ? result.Value as string : result.Message);
    }

    // get string from stream
    // note: must not close stream!
    string ReadStream(Stream stream) {
      var reader = new StreamReader(stream, Encoding.UTF8, false);
      var ret = reader.ReadToEnd();
      //stream.Close(); // NO!!!
      return ret;
    }

    // get query params as array of key value pair
    KeyValuePair<string, string>[] GetQueryParams() {
      var qparm = WebOperationContext.Current.IncomingRequest.UriTemplateMatch.QueryParameters;
      var kvps = Enumerable.Range(0, qparm.Count)
        .Select(x => new KeyValuePair<string, string>(qparm.GetKey(x), qparm.Get(x)))
        .ToArray();
      return kvps.Length > 0 ? kvps : null;
    }

    // logging code for kvp
    string KvpToString(IEnumerable<KeyValuePair<string, string>> qparams) {
      if (qparams == null) return "{}";
      var x = qparams.Select(p => String.Format("{0}={1}", p.Key, p.Value)).ToArray();
      return "{" + string.Join(", ", x) + "}";
    }

    // emit string as response
    Stream ToStream(string content) {
      var bytes = Encoding.UTF8.GetBytes(content);
      return new MemoryStream(bytes);
    }
  }

}
