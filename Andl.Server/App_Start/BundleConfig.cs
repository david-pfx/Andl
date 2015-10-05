using System.Web;
using System.Web.Optimization;

namespace Andl.Server {
  public class BundleConfig {
    // For more information on bundling, visit http://go.microsoft.com/fwlink/?LinkId=301862
    public static void RegisterBundles(BundleCollection bundles) {
      bundles.Add(new ScriptBundle("~/bundles/jquery").Include(
                  "~/Scripts/jquery-{version}.js"));

      // Use the development version of Modernizr to develop with and learn from. Then, when you're
      // ready for production, use the build tool at http://modernizr.com to pick only the tests you need.
      bundles.Add(new ScriptBundle("~/bundles/modernizr").Include(
                  "~/Scripts/modernizr-*"));

      bundles.Add(new ScriptBundle("~/bundles/bootstrap").Include(
                "~/Scripts/bootstrap.js",
                "~/Scripts/respond.js"));

      bundles.Add(new StyleBundle("~/Content/css").Include(
                "~/Content/bootstrap.css",
                "~/Content/site.css"));
      // New code:
      bundles.Add(new ScriptBundle("~/bundles/spapi").Include(
                "~/Scripts/knockout-{version}.js",
                "~/Scripts/appspapi.js"));
      bundles.Add(new ScriptBundle("~/bundles/sprest").Include(
                "~/Scripts/knockout-{version}.js",
                "~/Scripts/appsprest.js"));
      bundles.Add(new ScriptBundle("~/bundles/emprest").Include(
                "~/Scripts/knockout-{version}.js",
                "~/Scripts/appemprest.js"));
    }
  }
}
