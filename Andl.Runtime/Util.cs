using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Andl.Runtime {
  public static class UtilExtensions {
    public static string Shorten(this string text, int len) {
      if (text.Length <= len) return text;
      return text.Substring(0, len - 3) + "...";
    }
  }
}
