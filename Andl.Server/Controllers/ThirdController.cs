using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace Andl.Server.Controllers {
  public class ThirdController : ApiController {
    public IHttpActionResult Get(string catalog, string name, string id) {
      return Ok();
    }
  }
}
