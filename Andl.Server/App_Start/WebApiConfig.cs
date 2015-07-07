﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;

namespace Andl.Server {
  public static class WebApiConfig {
    public static void Register(HttpConfiguration config) {
      // Web API configuration and services

      // Web API routes
      config.MapHttpAttributeRoutes();

      config.Routes.MapHttpRoute(
          name: "RestApi",
          routeTemplate: "rest/{name}/{id}",
          defaults: new { controller = "rest", id = RouteParameter.Optional }
      );
      config.Routes.MapHttpRoute(
          name: "DefaultApi",
          routeTemplate: "api/{catalog}/{name}",
          defaults: new { controller = "andl" }
      );
    }
  }
}
