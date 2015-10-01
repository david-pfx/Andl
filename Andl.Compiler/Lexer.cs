/// Andl is A New Data Language. See andl.org.
///
/// Copyright © David M. Bennett 2015 as an unpublished work. All rights reserved.
///
/// If you have received this file directly from me then you are hereby granted 
/// permission to use it for personal study. For any other use you must ask my 
/// permission. Not to be copied, distributed or used commercially without my 
/// explicit written permission.
///
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Andl.Runtime;

namespace Andl.Compiler {
  public enum TokenTypes {
    // first group all count as white space (White must be last)
    Nul, LINE, Directive, Bad, White, 
    // these are ungrouped
    Number, HexNumber, Identifier, Operator, Punctuation, Binary, Time,
    // this group is in order for aggregation of tokens -- nothing after here
    IdLit, CharDouble, CharSingle, CharHex, CharDec,
  }

  /// <summary>
  /// Implement a single token, including how to extract a value from it
  /// </summary>
  public struct Token {
    public const string EolName = ":eol";
    public const string EofName = ":eof";

    public string Value { get; set; }
    public TokenTypes TokenType { get; set; }
    public int LineNumber { get; set; }
    public override string ToString() {
      return String.Format("'{0}':{1}", Value, TokenType);
    }
    public bool IsDefinable { 
      get { 
        return TokenType == TokenTypes.Identifier || TokenType == TokenTypes.IdLit || TokenType == TokenTypes.Operator || TokenType == TokenTypes.Punctuation; 
      }
    }
    // Real white space is discarded, but some other things left in still count as white
    public bool IsWhite { get { return TokenType <= TokenTypes.White; } }

    public static Token Create(string name, TokenTypes type, int lineno) {
      var ret = new Token { Value = name, TokenType = type, LineNumber = lineno };
      if (!ret.IsValid) ret.TokenType = TokenTypes.Bad;
      return ret;
    }

    public Decimal? GetNumber() {
      decimal dret;
      if (TokenType == TokenTypes.Number && Decimal.TryParse(Value, out dret))
        return dret;
      Int64 iret;
      if (TokenType == TokenTypes.HexNumber && Int64.TryParse(Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out iret))
        return Convert.ToDecimal(iret);
      return null;
    }
    public DateTime? GetTime() {
      DateTime tret;
      if (TokenType == TokenTypes.Time && DateTime.TryParse(Value, out tret))
        return tret;
      return null;
    }
    public byte[] GetBinary() {
      var b = new byte[Value.Length / 2];
      for (var i = 0; i < b.Length; ++i) {
        int n;
        if (!Int32.TryParse(Value.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n))
          return null;
        b[i] = (byte)n;
      }
      return b;
    }

    public bool IsValid {
      get {
        if (TokenType == TokenTypes.Bad) return false;
        if (TokenType == TokenTypes.Number) return GetNumber() != null;
        if (TokenType == TokenTypes.Time) return GetTime() != null;
        if (TokenType == TokenTypes.Binary) return GetBinary() != null;
        return true;
      }
    }
  }

  /// <summary>
  /// Implement a lexer, that can be called to deliver a stream of tokens
  /// </summary>
  public class Lexer {
    SymbolTable _symbols;
    Catalog _catalog;
    bool _stop = false;
    List<Token> _tokenlist = new List<Token>();
    int _tokenindex = -1;  // must call GetNext() first
    Token _lasttoken = new Token();
    Symbol _currentsymbol = Symbol.None;
    
    public int LineNumber { get { return _tokenlist[_tokenindex].LineNumber;  } }

    // Create new lexer on given reader
    public static Lexer Create(TextReader reader, SymbolTable symbols, Catalog catalog) {
      var lexer = new Lexer() {
        _symbols = symbols,
        _catalog = catalog,
      };
      lexer.InitRegexTable();
      lexer.PrepareTokens(reader);
      lexer.Next();
      return lexer;
    }

    // Insert a file into this one
    // Not a nice way to do it...
    public bool Include(string path) {
      if (!File.Exists(path)) return false;
      using (StreamReader sr = File.OpenText(path)) {
        Include(sr);
      }
      return true;
    }

    public void Include(TextReader reader) {
      var svtl = _tokenlist;
      _tokenlist = new List<Token>();
      PrepareTokens(reader, true);
      svtl.InsertRange(_tokenindex + 1, _tokenlist);
      _tokenlist = svtl;
      MoveNext();
    }

    public Symbol Current {
      get { return _currentsymbol; }
    }

    public Symbol LookAhead(int n) {
      var pos = LookNext(n);
      return (pos == 0) ? _currentsymbol 
        : _symbols.GetSymbol(_tokenlist[pos]);
      //var pos = Math.Min(_tokenlist.Count-1, _tokenindex + n);
      //return (pos < 0) ? Symbol.None
      //  : (pos == 0) ? _currentsymbol
      //  : _symbols.GetSymbol(_tokenlist[pos]);
    }

    public void Next() {
      MoveNext();
      _currentsymbol = _symbols.GetSymbol(_tokenlist[_tokenindex]);
      Logger.WriteLine(4, "Token=<{0}> Sym=<{1}>", _tokenlist[_tokenindex], _currentsymbol);
    }

    public void Back() {
      Logger.Assert(_tokenindex > 0);
      Logger.WriteLine(4, "Token -- back");
      _tokenindex--;
      _currentsymbol = _symbols.GetSymbol(_tokenlist[_tokenindex]);
    }

    ///=================================================================
    /// Implementation
    /// 

    public class RegexRow {
      public TokenTypes TokenType;
      public Regex Re;
    }
    List<RegexRow> _regextable = new List<RegexRow>();

    void AddRegex(TokenTypes tokentype, string regex, RegexOptions options = RegexOptions.None) {
      _regextable.Add(new RegexRow { 
        TokenType = tokentype, 
        Re = new Regex(regex, options | RegexOptions.IgnorePatternWhitespace) 
      });
    }

    void InitRegexTable() {
      AddRegex(TokenTypes.White, @"\G( [\x00-\x20]+ | //.* )");             // whitespace and control chars and comments
      AddRegex(TokenTypes.Directive, @"\G\#.*");                            // directive
      AddRegex(TokenTypes.CharSingle, @"\G'( [^'\x00-\x1f]* )'");           // any non-control chars in single quotes
      AddRegex(TokenTypes.CharDouble, @"\G\x22( [^\x22\x00-\x1f]* )\x22");  // any non-control chars in double quotes (0x22)
      AddRegex(TokenTypes.CharHex, @"\Gh'( [0-9a-f \x20]* )'", RegexOptions.IgnoreCase);  // wide hex literal
      AddRegex(TokenTypes.CharDec, @"\Gd'( [0-9 \x20]* )'");                // decimal literal
      AddRegex(TokenTypes.Binary, @"\Gb'( [0-9a-f]* )'", RegexOptions.IgnoreCase);        // binary literal
      AddRegex(TokenTypes.IdLit, @"\Gi'( [^'\x00-\x1f]* )'");               // identifier via literal
      AddRegex(TokenTypes.Time, @"\Gt'( [a-z0-9/:. -]+ )'", RegexOptions.IgnoreCase);      // time literal
      AddRegex(TokenTypes.Number, @"\G([.]?[0-9]+[0-9.]*)");                // various kinds of number
      AddRegex(TokenTypes.HexNumber, @"\G[$]([0-9]+[0-9a-f]*)", RegexOptions.IgnoreCase);    // hex number
      AddRegex(TokenTypes.Operator, @"\G[-+=<>:*~][-+=<>:*~]?");            // operators, could be two char
      AddRegex(TokenTypes.Identifier, @"\G[a-z_$@#^][a-z0-9_@#$^%&?!~`|]*", RegexOptions.IgnoreCase); // identifiers, must start with alpha etc
      AddRegex(TokenTypes.Punctuation, @"\G[^\x00-\0x20]");                 // one single char not already matched, but not a space or CC    
    }

    // Tokenise the input and keep until asked
    void PrepareTokens(TextReader reader, bool include = false) {
      var lineno = 0;
      for (var line = reader.ReadLine(); line != null; line = reader.ReadLine()) {
        lineno++;
        AddToken(line, TokenTypes.LINE, lineno);
        Match m = Match.Empty;
        for (var col = 0; col < line.Length; col += m.Length) {
          var tt = TokenTypes.Nul;
          for (int i = 0; i < _regextable.Count && tt == TokenTypes.Nul; ++i) {
            m = _regextable[i].Re.Match(line, col);
            if (m.Success) {
              tt = _regextable[i].TokenType;
              if (tt != TokenTypes.White)
                AddToken(m.Groups[m.Groups.Count - 1].Value, tt, lineno); // Take the innermost/last group
            }
          }
        }
        AddToken(Token.EolName, TokenTypes.Punctuation, lineno);
      }
      if (!include) AddToken(Token.EofName, TokenTypes.Punctuation, lineno);
    }

    Regex rehex = new Regex("[\x20]*([0-9a-f]+)", RegexOptions.IgnoreCase);

    // Add token to list. Merge tokens here as needed.
    void AddToken(string name, TokenTypes type, int lineno) {
      if (type == TokenTypes.CharHex || type == TokenTypes.CharDec) {
        var m = rehex.Matches(name);
        var s = new StringBuilder();
        for (var i = 0; i < m.Count; ++i) {
          var g = m[i].Groups;
          var n = Int32.Parse(g[0].Value, type == TokenTypes.CharHex ? NumberStyles.HexNumber : NumberStyles.Integer);
          s.Append(Char.ConvertFromUtf32(n));
        }
        name = s.ToString();
        type = TokenTypes.CharDouble;
      } 

      // merge string tokens, but keep kind from first
      if (_lasttoken.TokenType >= TokenTypes.IdLit && type >= TokenTypes.CharDouble) {
        _lasttoken.Value += name;
        _tokenlist[_tokenlist.Count - 1] = _lasttoken;
      } else {
        var token = Token.Create(name, type, lineno);
        _tokenlist.Add(token);
        _lasttoken = token;
      }
    }

    // Step to next token, taking action as we go
    void MoveNext() {
      if (_stop) _tokenindex = _tokenlist.Count - 1;
      while (_tokenindex < _tokenlist.Count - 1) {
        ++_tokenindex;
        var token = _tokenlist[_tokenindex];
        if (token.TokenType == TokenTypes.LINE) {
          _symbols.Find("$lineno$").Value = NumberValue.Create(token.LineNumber);
          if (Logger.Level > 0)
            Console.WriteLine("{0,3}: {1}", token.LineNumber, token.Value);
        } else if (token.TokenType == TokenTypes.Directive) {
          Directive(token);
        } else if (token.TokenType == TokenTypes.Bad) {
          ErrLexer(token.LineNumber, "bad token '{0}'", token.Value);
        } else break;
      }
      Logger.Assert(!_tokenlist[_tokenindex].IsWhite);
      Logger.WriteLine(6, "Token=<{0}> <{1}>", _tokenlist[_tokenindex], Current);
    }

    // Lookahead N tokens with no action, return pos
    public int LookNext(int n) {
      var pos = _tokenindex + n;
      Logger.Assert(pos >= 0);
      while (pos < _tokenlist.Count && _tokenlist[pos].IsWhite)
        pos++;
      return pos < _tokenlist.Count ? pos : _tokenlist.Count - 1;
    }


    // Process line as directive, return true if so
    private bool Directive(Token token) {
      var line = token.Value;
      if (line.StartsWith("#")) {
        var cmd = line.Split(null);
        switch (cmd[0]) {
        case "#noisy": 
          Logger.Level = cmd.Length >= 2 ? int.Parse(cmd[1]) : 1;
          return true;
        case "#stop":
          if (cmd.Length >= 2) Logger.Level = int.Parse(cmd[1]);
          _stop = true;
          return true;
        case "#include":
          if (cmd.Length >= 2) {
            if (!Include(cmd[1]))
              ErrLexer(token.LineNumber, "cannot include '{0}'", cmd[1]);
          }
          return true;
        case "#catalog":
          _catalog.LoadFlag = !cmd.Contains("new");
          _catalog.SaveFlag = cmd.Contains("update");
          return true;
        default:
          ErrLexer(token.LineNumber, "bad directive: {0}", cmd[0]);
          return true;
        }
      }
      return false;
    }

    // Lexer error -- just discards token
    bool ErrLexer(int lineno, string message, params object[] args) {
      Logger.WriteLine("Error line {0}: {1}", lineno, String.Format(message, args));
      return true;
    }

  }

}
