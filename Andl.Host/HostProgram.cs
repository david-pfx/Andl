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
using Andl.API;
using System.Configuration;

namespace Andl.Host {
  public enum ResultCode {
    Ok, NotFound, BadRequest, Fail, Error
  };
  public struct Result {
    public ResultCode Code;
    public string Value;
    public static Result Create(ResultCode code, string value) {
      return new Result { Code = code, Value = value };
    }
    public static Result OK(string value) { return Create(ResultCode.Ok, value); }
  }

  /// <summary>
  /// Defined entry points
  /// </summary>
  [ServiceContract]
  public interface IRestContract {
    [WebInvoke(UriTemplate = "{catalog}/{name}/{id=null}")]
    Stream DoPost(string catalog, string name, string id, Stream body);

    [WebInvoke(Method = "PUT", UriTemplate = "{catalog}/{name}/{id=null}",
      ResponseFormat = WebMessageFormat.Json)]
    Stream DoPut(string catalog, string name, string id, Stream body);

    [WebInvoke(Method = "GET", UriTemplate = "{catalog}/{name}/{id=null}")]
    Stream DoGet(string catalog, string name, string id);

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

    //--- common functions

    static readonly Dictionary<ResultCode, HttpStatusCode> _codedict = new Dictionary<ResultCode, HttpStatusCode> {
      { ResultCode.Ok, HttpStatusCode.OK },
      { ResultCode.BadRequest, HttpStatusCode.BadRequest },
      { ResultCode.NotFound, HttpStatusCode.NotFound },
      { ResultCode.Error, HttpStatusCode.InternalServerError },
    };

    Stream Common(string method, string catalog, string name, string id, Stream body = null) {
      string content = (body == null) ? null : ReadStream(body);
      var qparams = GetQueryParams();

      // TODO: implement catalog
      var result = HostProgram.Gateway.JsonCall(method, name, id, qparams, content);

      WebOperationContext.Current.OutgoingResponse.StatusCode = result.Ok ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
      WebOperationContext.Current.OutgoingResponse.ContentType = "application/json";
      return ToStream(result.Ok ? result.Value as string : result.Message);
      //WebOperationContext.Current.OutgoingResponse.StatusCode = _codedict[result.Code];
      //WebOperationContext.Current.OutgoingResponse.ContentType = "application/json";
      //return ToStream(result.Value);
    }

#if testing
    Result Process(string method, string catalog, string name, string id, List<KeyValuePair<string, string>> qparams, string content) {
      Console.WriteLine("Server {0}: {1},{2},{3},{4} <{5}>", method, catalog, name, id, KvpToString(qparams), content);
      if (name.Contains("bad")) return Result.Create(ResultCode.BadRequest, "that was a bad one");
      return Result.OK("[{\"Sid\":\"" + id + "\",\"SNAME\":\"newname\",\"STATUS\":0,\"CITY\":\"\"}]");
    }
#endif

    // get string from stream
    // note: must not close stream!
    string ReadStream(Stream stream) {
      var reader = new StreamReader(stream, Encoding.UTF8, false);
      var ret = reader.ReadToEnd();
      //stream.Close(); // NO!!!
      return ret;
    }

    KeyValuePair<string, string>[] GetQueryParams() {
      var qparm = WebOperationContext.Current.IncomingRequest.UriTemplateMatch.QueryParameters;
      var kvps = Enumerable.Range(0, qparm.Count)
        .Select(x => new KeyValuePair<string, string>(qparm.GetKey(x), qparm.Get(x)))
        .ToArray();
      return kvps.Length > 0 ? kvps : null;
    }

    string KvpToString(IEnumerable<KeyValuePair<string, string>> qparams) {
      if (qparams == null) return "[]";
      var x = qparams.Select(p => String.Format("{0}={1}", p.Key, p.Value)).ToArray();
      return "[" + string.Join(", ", x) + "]";
    }

    Stream ToStream(string content) {
      var bytes = Encoding.UTF8.GetBytes(content);
      return new MemoryStream(bytes);
    }
  }

  ///===========================================================================
  /// <summary>
  /// Implement calling program
  /// </summary>
  class HostProgram {
    public static Runtime Gateway;

    static void Main(string[] args) {
      AppStartup();
      string baseAddress = "http://" + Environment.MachineName + ":8000/api";
      var host = CreateHost(baseAddress, "");
      host.Open();
      Console.WriteLine("Host opened, Enter to close.");
      SendTests(baseAddress);
      Console.ReadLine();
      host.Close();
    }

    static WebServiceHost CreateHost(string address, string endpoint) {
      var host = new WebServiceHost(typeof(RawDataService), new Uri(address));
      host.AddServiceEndpoint(typeof(IRestContract), GetBinding(), endpoint);
      ServiceDebugBehavior sdb = host.Description.Behaviors.Find<ServiceDebugBehavior>();
      sdb.HttpHelpPageEnabled = false;
      return host;
    }

    // Force content mapping to use Raw always (otherwise it traps any Json or XML.
    // see http://blogs.msdn.com/b/carlosfigueira/archive/2008/04/17/wcf-raw-programming-model-receiving-arbitrary-data.aspx
    public class RawMapper : WebContentTypeMapper {
      public override WebContentFormat GetMessageFormatForContentType(string contentType) {
        if (contentType == "application/json") return WebContentFormat.Raw;
        return WebContentFormat.Default;
        //return WebContentFormat.Raw; // always
      }
    }

    static Binding GetBinding() {
      CustomBinding result = new CustomBinding(new WebHttpBinding());
      var wmebe = result.Elements.Find<WebMessageEncodingBindingElement>();
      wmebe.ContentTypeMapper = new RawMapper();
      return result;
    }

    static readonly Dictionary<string, string> _settingsdict = new Dictionary<string, string> {
      { "DatabasePath", "DatabasePath" },
      { "DatabasePathSqlFlag", "DatabaseSqlFlag" },
    };

    static void AppStartup() {
      var appsettings = ConfigurationManager.AppSettings;
      var settings = appsettings.AllKeys
        .Where(k => _settingsdict.ContainsKey(k))
        .ToDictionary(k => _settingsdict[k], k => appsettings[k]);
      Gateway = Andl.API.Runtime.StartUp(settings);
      Gateway.JsonReturnFlag = true;
    }

    static void SendTests(string baseaddress) {
      SendRequest(baseaddress + "/main/supplier", "GET");
      SendRequest(baseaddress + "/main/supplier/S2", "GET");
      var newsupp = "[{'Sid':'S9','SNAME':'Adolph','STATUS':99,'CITY':'Melbourne'}]".Replace('\'', '"');
      SendRequest(baseaddress + "/main/supplier/x", "POST", "json", newsupp);
      SendRequest(baseaddress + "/main/supplier/", "POST", "json", newsupp);
      SendRequest(baseaddress + "/main/supplier", "POST", "json", newsupp);
      SendRequest(baseaddress + "/main/supplier", "DELETE");
      SendRequest(baseaddress + "/main/supplier/S9", "DELETE");
      SendRequest(baseaddress + "/main/supplier/S9", "GET");
      SendRequest(baseaddress + "/main/supplier", "PUT", "json", newsupp);
      SendRequest(baseaddress + "/main/supplier/S9", "PUT", "json", newsupp);
      SendRequest(baseaddress + "/main/supplier/S9", "GET");
      SendRequest(baseaddress + "/main/part", "GET");
      SendRequest(baseaddress + "/main/part?PNAME=S.*", "GET");
      SendRequest(baseaddress + "/main/xsupplier", "DELETE");
#if tests
      SendRequest(baseaddress + "/main/badsupplier", "GET");
      SendRequest(baseaddress + "/main/badsupplier", "POST", "json", "post 1");
#endif
    }

    static void SendRequest(string address, string verb, string kind = null, string content = null) {
      Console.WriteLine("Client: Send {0} {1} {2}", address, verb, kind);
      HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(address);
      req.Method = verb;

      if (kind != null) {
        Stream reqStream = req.GetRequestStream();
        var bytes = Encoding.UTF8.GetBytes(content);
        reqStream.Write(bytes, 0, bytes.Length);
        reqStream.Close();
        //byte[] fileToSend = null;

        //switch (kind) {
        //case "text":
        //  req.ContentType = "text/plain";

        //  fileToSend = new byte[12345];
        //  for (int i = 0; i < fileToSend.Length; i++) {
        //    fileToSend[i] = (byte)('a' + (i % 26));
        //  }
        //  break;
        //case "json":
        //  req.ContentType = "application/json";
        //  //req.ContentType = "json";
        //  fileToSend = Encoding.UTF8.GetBytes("[{\"Sid\":\"" + content + "\",\"SNAME\":\"\",\"STATUS\":0,\"CITY\":\"\"}]");
        //  break;
        //}
        //reqStream.Write(fileToSend, 0, fileToSend.Length);
        //reqStream.Close();
      }
      //--- send it
      HttpWebResponse resp;
      try {
        resp = (HttpWebResponse)req.GetResponse();
      } catch (WebException e) {
        resp = e.Response as HttpWebResponse;
      }

      Console.WriteLine("Client: Receive Response HTTP/{0} {1} {2} type {3} length {4}",
        resp.ProtocolVersion, (int)resp.StatusCode, resp.StatusDescription, resp.ContentType, resp.ContentLength);
      var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8);
      var body = sr.ReadToEnd();
      Console.WriteLine("Body: <{0}>\n", body);
      resp.Close();
    }
  }
}
