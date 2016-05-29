using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Andl.Common {
  // types of server supported
  public enum DatabaseKinds {
    Memory, Sqlite, Postgres
  }

  public static class UtilExtensions {
    public static string Format(this byte[] value) {
      var s = value.Select(b => String.Format("{0:x2}", b));
      return String.Join("", s);
    }

    // string join that works on any enumerable
    public static string Join<T>(this IEnumerable<T> values, string delim) {
      return String.Join(delim, values.Select(v => v.ToString()));
    }

    // truncate a string if too long
    public static string Shorten(this string argtext, int len) {
      var text = argtext.Replace('\n', '.');
      if (text.Length <= len) return text;
      return text.Substring(0, len - 3) + "...";
    }

    // safe parsing routines, return null on error
    public static DateTime? SafeDatetimeParse(this string s) {
      DateTime value;
      return DateTime.TryParse(s, out value) ? value as DateTime? : null;
    }

    public static decimal? SafeDecimalParse(this string s) {
      decimal value;
      return Decimal.TryParse(s, out value) ? value as decimal? : null;
    }

    public static int? SafeIntParse(this string s) {
      int value;
      return int.TryParse(s, out value) ? value as int? : null;
    }

    public static bool? SafeBoolParse(this string s) {
      bool value;
      return bool.TryParse(s, out value) ? value as bool? : null;
    }
  }
}
