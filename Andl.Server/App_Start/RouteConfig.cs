using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace Andl.Server {
  public class RouteConfig {
    public static void RegisterRoutes(RouteCollection routes) {
      routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

      routes.MapRoute(
          name: "Third",
          url: "api/{catalog}/{name}/{id}",
          defaults: new { controller = "Third", action = "Action", id = UrlParameter.Optional }
      );
      routes.MapRoute(
          name: "Rest",
          url: "{controller}/{action}/{id}",
          defaults: new { id = UrlParameter.Optional }
      );
      routes.MapRoute(
          name: "Default",
          url: "",
          defaults: new { controller = "Home", action = "Index" }
      );
      //routes.MapRoute(
      //    name: "Default",
      //    url: "{controller}/{catalog}/{action}/{id}",
      //    defaults: new { controller = "Home", action = "Index", Catalog = "default", id = UrlParameter.Optional }
      //);
    }
  }
}
