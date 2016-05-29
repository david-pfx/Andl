using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Andl.Gateway;
using Andl.Common;

namespace Andl.Client {
  /// <summary>
  /// Program that makes direct calls to the gateway
  /// </summary>
  class Program {
    static void Main(string[] args) {
      Console.WriteLine("Andl.Client");
      //new CallCompile().Repl();
      new CallCompile().Exec();
      //new CallNative().Exec();
      //CallFindSupplier();
      //CallFunc();
      //ShowCatalog();
    }
  }

  class CallCompile {
    GatewayBase _api;
    string DatabaseName = "Supplier";
    Dictionary<string, string> _settingsdict = new Dictionary<string, string> {
      { "Noisy", "0" },
    };

    internal void Exec() {
      _api = GatewayFactory.Create(DatabaseName, _settingsdict);
      Run("fact:0(n:0) => if(n<=1,1,n*fact(n-1))");
      Run("fact(6)");
    }

    internal void Repl() {
      _api = GatewayFactory.Create(DatabaseName, _settingsdict);
      for (; ; ) {
        Console.Write(">>>");
        var line = Console.ReadLine();
        Run(line);
      }
    }

    void Run(string program) {
      Console.WriteLine("{0}", program);
      ShowResult(_api.RunScript(program));
    }
        
    void ShowResult(Result result) {
      if (result.Ok)
        Console.WriteLine("{0}", result.Value);
      else Console.WriteLine("{0}", result.Message);
    }

  }

  class CallNative {
    string DatabaseName = "Supplier";
    Dictionary<string, string> _settingsdict = new Dictionary<string, string> {
      { "Noisy", "2" },
    };

    internal void Exec() {
      CallFindSupplier();
    }
    
    void CallFindSupplier() {
      Supplier[] s;
      var ret = FindSupplier("S1", out s);
      if (ret)
        Console.WriteLine("FindSupplier {0} {1} {2} {3} {4} ", ret, s[0].SNAME, s[0].STATUS, s[0].CITY, s[0].Sid);
      else Console.WriteLine("error!");
    }

    void CallFunc() {
      var api = GatewayFactory.Create(DatabaseName, _settingsdict);
      var args = ArgWriter.Create().Put("abcdef").Out();
      byte[] result;
      var ret = api.NativeCall("func", args, out result);
      var rr = ResultReader.Create(result);
      Console.WriteLine("Result={0}", rr.ReadText());
    }

    void ShowCatalog() {
      var api = GatewayFactory.Create(DatabaseName, _settingsdict);
      byte[] result;
      var ret = api.NativeCall("andl_catalog", new byte[0], out result);
    }

    public class Supplier {
      public string SNAME;
      public decimal STATUS;
      public string CITY;
      public string Sid;
    };

    bool FindSupplier(string id, out Supplier[] supplier) {
      var api = GatewayFactory.Create(DatabaseName, _settingsdict);
      byte[] args = ArgWriter.Create().Put(id).Out();
      byte[] result;
      supplier = null;
      if (!api.NativeCall("find_supplier", args, out result)) return false;
      int n;
      var r = ResultReader.Create(result).Get(out n);
      supplier = new Supplier[n];
      for (int i = 0; i < n; ++i) {
        supplier[i] = new Supplier();
        r.Get(out supplier[i].SNAME).Get(out supplier[i].STATUS).Get(out supplier[i].CITY).Get(out supplier[i].Sid);
      }
      return true;
    }
  }

  /// <summary>
  /// Implement writing values to an argument stream
  /// </summary>
  public class ArgWriter {
    BinaryWriter _writer;

    public Byte[] Out() {
      return (_writer.BaseStream as MemoryStream).ToArray();
    }

    public static ArgWriter Create() {
      return new ArgWriter {
        _writer = new BinaryWriter(new MemoryStream())
      };
    }

    Dictionary<Type, Action<ArgWriter, object>> _writerdict = new Dictionary<Type,Action<ArgWriter,object>> {
      { typeof(bool), (aw, v) => aw._writer.Write((bool)v) },
      { typeof(decimal), (aw, v) => aw._writer.Write((decimal)v) },
      { typeof(string), (aw, v) => aw._writer.Write((string)v) },
      { typeof(DateTime), (aw, v) => aw._writer.Write(((DateTime)v).ToBinary()) },
    };


    // binary
    public ArgWriter Put(byte[] value) {
      _writer.Write(value.Length);
      _writer.Write(value);
      return this;
    }
    // bool
    ArgWriter Put(bool value) {
      _writer.Write(value);
      return this;
    }
    // number
    ArgWriter Put(decimal value) {
      _writer.Write(value);
      return this;
    }
    // text
    public ArgWriter Put(string value) {
      _writer.Write(value);
      return this;
    }
    // time
    ArgWriter Put(DateTime value) {
      _writer.Write(value.ToBinary());
      return this;
    }
  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Implement reading values from a result stream
  /// </summary>
  public class ResultReader {
    BinaryReader _reader;

    public static ResultReader Create(byte[] buffer) {
      return new ResultReader { 
        _reader = new BinaryReader(new MemoryStream(buffer)) 
      };
    }

    // read base types
    public ResultReader Get(out byte[] value) {
     var length = _reader.ReadInt32(); 
     value = _reader.ReadBytes(length);
      return this;
    }
    public ResultReader Get(out bool value) {
      value = _reader.ReadBoolean();
      return this;
    }
    public ResultReader Get(out string value) {
      value = _reader.ReadString();
      return this;
    }
    public ResultReader Get(out DateTime value) {
      value = DateTime.FromBinary(_reader.ReadInt64());
      return this;
    }
    public ResultReader Get(out decimal value) {
      value = _reader.ReadDecimal();
      return this;
    }
    public ResultReader Get(out int value) {
      value = (int)_reader.ReadInt32();
      return this;
    }

    // read base types
    public byte[] ReadBinary() {
     var length = _reader.ReadInt32(); 
     return _reader.ReadBytes(length);
 
    }
    public bool ReadBool() {
      return _reader.ReadBoolean();
    }
    public string ReadText() {
      return _reader.ReadString();
    }
    public DateTime ReadTime() {
      return DateTime.FromBinary(_reader.ReadInt64());
    }
    public decimal ReadNumber() {
      return _reader.ReadDecimal();
    }
  }

}
