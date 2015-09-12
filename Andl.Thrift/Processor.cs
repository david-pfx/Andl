using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Thrift;
using Thrift.Protocol;
using Thrift.Server;
using Thrift.Transport;
using Andl.API;
using Andl.Runtime; // for TypedValueBuilder -- temp

namespace Andl.Thrift {
  public class Processor : TProcessor {
    Gateway _gateway;
    TypedValueBuilder _arguments;
    TypedValueBuilder _result;

    public Processor(Gateway gateway) {
      _gateway = gateway;
    }

    public bool Process(TProtocol iprot, TProtocol oprot) {
      try {
        TMessage msg = iprot.ReadMessageBegin();
        _arguments = _gateway.GetTypedValueBuilder(msg.Name);
        if (_arguments == null) {
          TProtocolUtil.Skip(iprot, TType.Struct);
          iprot.ReadMessageEnd();
          TApplicationException x = new TApplicationException(TApplicationException.ExceptionType.UnknownMethod, "Invalid method name: '" + msg.Name + "'");
          oprot.WriteMessageBegin(new TMessage(msg.Name, TMessageType.Exception, msg.SeqID));
          x.Write(oprot);
          oprot.WriteMessageEnd();
          oprot.Transport.Flush();
          return true;
        }
        ProcessMessage(msg, iprot, oprot);
        //fn(msg.SeqID, iprot, oprot);
      } catch (IOException) {
        return false;
      }
      return true;
    }

    private void ProcessMessage(TMessage msg, TProtocol iprot, TProtocol oprot) {
      iprot.ReadStructBegin(); //???
      ReadFields(iprot);
      iprot.ReadStructEnd();
      iprot.ReadMessageEnd();
      var result = _gateway.BuilderCall(msg.Name, _arguments, out _result);
      oprot.WriteMessageBegin(new TMessage(msg.Name, TMessageType.Reply, msg.SeqID));
      WriteResult(oprot, result, msg);
      oprot.WriteMessageEnd();
      oprot.Transport.Flush();


      //find_supplier_args args = new find_supplier_args();
      //args.Read(iprot);
      //iprot.ReadMessageEnd();
      //find_supplier_result result = new find_supplier_result();
      //result.Success = iface_.find_supplier(args.Sid);
      //oprot.WriteMessageBegin(new TMessage("find_supplier", TMessageType.Reply, seqid));
      //result.Write(oprot);
      //oprot.WriteMessageEnd();
      //oprot.Transport.Flush();
    }

    // Read arguments according to field type and ID
    private void ReadFields(TProtocol iprot) {
      while (true) {
        var field = iprot.ReadFieldBegin();
        // Note: Thrift fields have to count from 1
        var colno = field.ID - 1;
        switch (field.Type) {
        case TType.Stop: return;
        case TType.Bool: _arguments.SetBool(colno, iprot.ReadBool()); break;
        case TType.Byte: _arguments.SetNumber(colno, iprot.ReadByte()); break;
        case TType.Double: _arguments.SetNumber(colno, (decimal)iprot.ReadDouble()); break;
        case TType.I64: _arguments.SetTime(colno, new DateTime(iprot.ReadI64())); break;
        case TType.String: _arguments.SetText(colno, iprot.ReadString()); break;
        case TType.List: 
          var tlist = iprot.ReadListBegin();
          _arguments.SetListBegin(colno);
          for (var i = 0; i < tlist.Count; ++i) {
            iprot.ReadStructBegin();
            _arguments.SetStructBegin(colno);
            ReadFields(iprot);
            iprot.ReadStructEnd();
            _arguments.SetStructEnd();
          }
          iprot.ReadListEnd();
          _arguments.SetListEnd();
          break;
        case TType.Struct: 
          iprot.ReadStructBegin();
          _arguments.SetStructBegin(colno);
          ReadFields(iprot);
          _arguments.SetStructEnd();
          iprot.ReadStructEnd();
          break;
        }
        iprot.ReadFieldEnd();
      }
    }

    //public void Read(TProtocol iprot) {
    //  TField field;
    //  iprot.ReadStructBegin();
    //  while (true) {
    //    field = iprot.ReadFieldBegin();
    //    if (field.Type == TType.Stop) {
    //      break;
    //    }
    //    switch (field.ID) {
    //    case 1:
    //      if (field.Type == TType.String) {
    //        Sid = iprot.ReadString();
    //      } else {
    //        TProtocolUtil.Skip(iprot, field.Type);
    //      }
    //      break;
    //    default:
    //      TProtocolUtil.Skip(iprot, field.Type);
    //      break;
    //    }
    //    iprot.ReadFieldEnd();
    //  }
    //  iprot.ReadStructEnd();
    //}

    private void WriteResult(TProtocol oprot, bool ok, TMessage msg) {
      TStruct struc = new TStruct(msg.Name + "_result");
      oprot.WriteStructBegin(struc);
      if (ok) {
        WriteFields(oprot, true);
      }
      oprot.WriteStructEnd();
    }

    // Assumes TType uses 4 bits
    const TType TTBINARY = TType.String | (TType)0x20;
    const TType TTUSER = TType.Struct | (TType)0x20;
    const TType TTMASK = (TType)0x0f;

    static readonly Dictionary<string, TType> _typedict = new Dictionary<string, TType> {
        { "bool", TType.Bool },
        { "binary", TTBINARY },
        { "number", TType.Double },
        { "time", TType.I64 },
        { "text", TType.String },
        { "tuple", TType.Struct },
        { "relation", TType.List },
        { "user", TTUSER },
        { "void", TType.Void },
    };

    // Write the fields for a structure, recursively
    void WriteFields(TProtocol oprot, bool isspecial = false) {
      for (var i = 0; i < _result.StructSize; ++i) {
        var datatype = _result.DataTypes[i];
        var ttype = _typedict[datatype.BaseName];
        if (ttype == TType.Void)
          break;
        // note: Thrift fields count from 1, but Success is special
        TField field = new TField { 
          Name = (isspecial) ? "Success" : _result.Names[i], 
          Type = ttype & TTMASK, 
          ID = (short)(isspecial ? 0 : i + 1)
        };
        oprot.WriteFieldBegin(field);
        switch (ttype) {
        case TType.Bool: oprot.WriteBool(_result.GetBool(i)); break;
        case TTBINARY: oprot.WriteBinary(_result.GetBinary(i)); break;
        case TType.Double: oprot.WriteDouble((double)_result.GetNumber(i)); break;
        case TType.I64: oprot.WriteI64(_result.GetTime(i).Ticks); break;
        case TType.String: oprot.WriteString(_result.GetText(i)); break;
        case TType.Struct:
        case TTUSER:
          _result.GetStructBegin(i);
          oprot.WriteStructBegin(new TStruct { Name = datatype.GenCleanName });
          WriteFields(oprot);
          oprot.WriteStructEnd();
          _result.GetStructEnd();
          break;
        case TType.List:
          _result.GetListBegin(i);
          oprot.WriteListBegin(new TList { 
            Count = _result.ListSize, ElementType = TType.Struct 
          });
          while (!_result.Done) {
            _result.GetStructBegin(i);
            // this will be the name for relation type, but should be the tuple type
            oprot.WriteStructBegin(new TStruct { Name = datatype.GenCleanName });
            WriteFields(oprot);
            oprot.WriteStructEnd();
            _result.GetStructEnd();
          }
          oprot.WriteListEnd();
          _result.GetListEnd();
          break;
        }
        oprot.WriteFieldEnd();
      }
      oprot.WriteFieldStop();
    }

    //public void Write(TProtocol oprot) {
    //  TStruct struc = new TStruct("find_supplier_result");
    //  oprot.WriteStructBegin(struc);
    //  TField field = new TField();

    //  if (this.__isset.success) {
    //    if (Success != null) {
    //      field.Name = "Success";
    //      field.Type = TType.List;
    //      field.ID = 0;
    //      oprot.WriteFieldBegin(field);
    //      {
    //        oprot.WriteListBegin(new TList(TType.Struct, Success.Count));
    //        foreach (Supplier _iter3 in Success) {
    //          _iter3.Write(oprot);
    //        }
    //        oprot.WriteListEnd();
    //      }
    //      oprot.WriteFieldEnd();
    //    }
    //  }
    //  oprot.WriteFieldStop();
    //  oprot.WriteStructEnd();
    //}

  }
}
