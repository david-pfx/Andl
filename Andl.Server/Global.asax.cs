using Andl.Gateway;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace Andl.Server {
  public class WebApiApplication : System.Web.HttpApplication {
    protected void Application_Start() {
      AreaRegistration.RegisterAllAreas(); 
      GlobalConfiguration.Configure(WebApiConfig.Register);
      FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
      RouteConfig.RegisterRoutes(RouteTable.Routes);
      BundleConfig.RegisterBundles(BundleTable.Bundles);

      AppStartup();
    }

    void AppStartup() {
      var appsettings = ConfigurationManager.AppSettings;
      var settings = appsettings.AllKeys
        .ToDictionary(k => k, v => appsettings[v]);
      settings["RootFolder"] = Server.MapPath(null);
      GatewayManager.AddGateways(settings);
    }

    // Access the required database
    public static GatewayBase GetGateway(string database) {
      return GatewayManager.GetGateway(database);
    }

  }
}
