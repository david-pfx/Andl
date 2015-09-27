using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Andl.Sqlite;

namespace Andl.Runtime {
  public class Templater {
    public string Template { get; protected set; }
    protected StringBuilder _builder = new StringBuilder();

    //static Dictionary<string, string> _templates = new Dictionary<string, string> {
    //  { "Create",         "DROP TABLE IF EXISTS [<table>] \n" +
    //                      "CREATE TABLE [<table>] ( <coldefs>, UNIQUE ( <colnames> ) ON CONFLICT IGNORE )" },
    //  { "SelectAll",      "SELECT <namelist> FROM [<table>]" },
    //  { "SelectRename",   "SELECT <namelist> FROM <select>" },
    //  { "SelectAs",       "SELECT DISTINCT <namelist> FROM <select>" },
    //  { "SelectAsGroup",  "SELECT DISTINCT <namelist> FROM <select> <groupby>" },
    //  { "SelectJoin",     "SELECT DISTINCT <namelist> FROM <select1> JOIN <select2> <using>" },
    //  { "SelectAntijoin", "SELECT DISTINCT <namelist> FROM <select1> [_a_] WHERE NOT EXISTS (SELECT 1 FROM <select2> [_b_] WHERE <nameeqlist>)" },
    //  { "SelectSet",      "<select1> <setop> <select2>" },
    //  { "SelectSetName",  "SELECT DISTINCT <namelist> FROM <select1> <setop> SELECT <namelist> FROM <select2>" },
    //  { "SelectCount",    "SELECT COUNT(*) FROM <select>" },
    //  { "SelectOneWhere", "SELECT 1 <whereexist>" },

    //  { "InsertNamed",    "INSERT INTO [<table>] ( <namelist> ) <select>" },
    //  { "InsertSelect",   "INSERT INTO [<table>] <select>" },
    //  { "InsertValues",   "INSERT INTO [<table>] ( <namelist> ) VALUES ( <valuelist> )" },
    //  { "InsertJoin",     "INSERT INTO [<table>] ( <namelist> ) <select>" },
    //  { "Delete",         "DELETE FROM [<table>] WHERE <pred>" },
    //  { "Update",         "UPDATE [<table>] SET <namesetlist> WHERE <pred>" },

    //  { "WhereExist",     "WHERE <not> EXISTS ( <select1> <setop> <select2> )" },
    //  { "WhereExist2",    "WHERE <not> EXISTS ( <select1> <setop> <select2> ) AND <not> EXISTS ( <select2> <setop> <select1> )" },
    //  { "Where",          "WHERE <expr>" },
    //  { "Using",          "USING ( <namelist> )" },

    //  { "OrderBy",        "ORDER BY <ordcols>" },
    //  { "GroupBy",        "GROUP BY <grpcols>" },
    //  { "EvalFunc",       "<func>(<lookups>)" },
    //  { "Coldef",         "[<colname>] <coltype>" },
    //  { "Name",           "[<name>]" },
    //  { "NameEq",         "[_a_].[<name>] = [_b_].[<name>]" },
    //  { "NameAs",         "[<name1>] AS [<name2>]" },
    //  { "NameSet",        "[<name1>] = [<name2>]" },
    //  { "Value",          "<value>" },
    //  { "Param",          "?<param>" },
    //};

    public override string ToString() {
      return _builder.ToString();
    }

    //public static Templater Create(string template) {
    //  var t = new Templater { Template = _templates[template] };
    //  return t;
    //}

    public void Append(string text) {
      _builder.Append(text);
    }

    //public static string Process(Dictionary<string, string> templates, string template, 
    //  Dictionary<string, SubstituteDelegate> dict, int index = 0) {
    //  Logger.Assert(templates.ContainsKey(template), template);
    //  var t = new Templater { Template = templates[template] };
    //  return t.Process(dict, index).ToString();
    //}

    public Templater Process(Dictionary<string, SubstituteDelegate> dict, int index = 0) {
      var pos = 0; 
      while (true) {
        var apos = Template.IndexOf('<', pos);
        if (apos == -1) {
          _builder.Append(Template, pos, Template.Length - pos);
          break;
        } else {
          _builder.Append(Template, pos, apos - pos);
          var bpos = Template.IndexOf('>', ++apos);
          var token = Template.Substring(apos, bpos - apos);
          Logger.Assert(dict.ContainsKey(token), token);
         _builder.Append(dict[token](index));
          pos = bpos + 1;
        }
      }
      return this;
    }

    public Templater Process(Dictionary<string, SubstituteDelegate> dict, int howmany, string delim) {
      for (var i = 0; i < howmany; ++i) {
        if (i > 0)
          _builder.Append(delim);
        Process(dict, i);
      }
      return this;
    }
  }

}
