using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using Andl.Gateway;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace Andl.Server.Controllers {
  public class KeyValue {
    public string Key;
    public string Value;
  }

  /// <summary>
  /// Implement a controller that presents a REST/JSON interface
  /// </summary>
  public class RestController : ApiController {

    // GET: rest/name
    public IHttpActionResult Get(string database, string name) {
      var query = Request.GetQueryNameValuePairs().ToArray();
      return Common("get", database, name, null, query.Count() > 0 ? query : null);
    }

    // GET: rest/name/5
    public IHttpActionResult Get(string database, string name, string id) {
      return Common("get", database, name, id);
    }

    // POST: rest/name
    public async Task<IHttpActionResult> Post(string database, string name) {
      var body = await Request.Content.ReadAsStringAsync();
      return Common("post", database, name, null, null, body);
    }

    // PUT: rest/name/5
    public async Task<IHttpActionResult> Put(string database, string name, string id) {
      var body = await Request.Content.ReadAsStringAsync();
      return Common("put", database, name, id, null, body);
    }

    // DELETE: rest/name/5
    public IHttpActionResult Delete(string database, string name, string id) {
      return Common("delete", database, name, id);
    }

    // Common code for all requests
    IHttpActionResult Common(string method, string database, string name, string id, KeyValuePair<string, string>[] query = null, string jsonbody = null) {

      var gateway = WebApiApplication.GetGateway(database);
      var ret = (gateway == null) ? Gateway.Result.Failure("database not found: " + database)
        : gateway.JsonCall(method, name, id, query, jsonbody);
      if (ret.Ok) return Ok(ret.Value);
      return BadRequest(ret.Message);
    }

  }
}
