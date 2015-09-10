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
  public class AndlProcessor : TProcessor {
    Gateway _gateway;
    TypedValueBuilder _builder;
    TypedValueBuilder _walker;

    public AndlProcessor(Gateway gateway) {
      _gateway = gateway;
    }

    public bool Process(TProtocol iprot, TProtocol oprot) {
      try {
        TMessage msg = iprot.ReadMessageBegin();
        _builder = _gateway.GetTypedValueBuilder(msg.Name);
        if (_builder == null) {
          //var argtypes = _gateway.GetArgumentTypes(msg.Name);
          //if (argtypes == null) {
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
      ReadMessage(iprot);
      iprot.ReadMessageEnd();
      var result = _gateway.BuilderCall(msg.Name, _builder, out _walker);
      oprot.WriteMessageBegin(new TMessage(msg.Name, TMessageType.Reply, msg.SeqID));
      WriteResult(oprot, msg);
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

    static readonly Dictionary<TType, Func<TProtocol, object>> _fieldreader = new Dictionary<TType, Func<TProtocol, object>> {
      { TType.Bool, (ip) => ip.ReadBool() },
      { TType.Byte, (ip) => ip.ReadByte() },
      { TType.Double, (ip) => ip.ReadDouble() },
      { TType.I16, (ip) => ip.ReadI16() },
      { TType.I32, (ip) => ip.ReadI32() },
      { TType.I64, (ip) => ip.ReadI64() },
      //{ TType.List, (ip) => ip.Read() },
      //{ TType.Map, (ip) => ip.Read() },
      //{ TType.Set, (ip) => ip.Read() },
      { TType.String, (ip) => ip.ReadString() },
      //{ TType.Struct, (ip) => ip.Read() },
      //{ TType.Void, (ip) => ip.Read() },
    };

    // Read arguments according to field type and ID
    private void ReadMessage(TProtocol iprot) {
      iprot.ReadStructBegin();
      while (true) {
        var field = iprot.ReadFieldBegin();
        if (field.Type == TType.Stop) break;
        switch (field.Type) {
        case TType.Bool: _builder.SetBool(field.ID, iprot.ReadBool()); break;
        case TType.Byte: _builder.SetNumber(field.ID, iprot.ReadByte()); break;
        case TType.Double: _builder.SetNumber(field.ID, (decimal)iprot.ReadDouble()); break;
        case TType.I16: _builder.SetNumber(field.ID, iprot.ReadI16()); break;
        case TType.I32: _builder.SetNumber(field.ID, iprot.ReadI32()); break;
        case TType.I64: _builder.SetNumber(field.ID, iprot.ReadI64()); break;
        case TType.String: _builder.SetText(field.ID, iprot.ReadString()); break;
        case TType.List: 
          _builder.SetListBegin(field.ID); 
          ReadMessage(iprot);
          _builder.SetListEnd();
          break;
        case TType.Struct: 
          _builder.SetStructBegin(field.ID);
          ReadMessage(iprot);
          _builder.SetStructEnd();
          break;
        default:
          break;
        }
        iprot.ReadFieldEnd();
      }
      iprot.ReadStructEnd();
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

    private void WriteResult(TProtocol oprot, TMessage msg) {
      TStruct struc = new TStruct(msg.Name + "_result");
      oprot.WriteStructBegin(struc);

      //if (result.Ok) {
      //  TField field = new TField { Name = "Success", Type = TType.List, ID = 0 };
      //  oprot.WriteFieldBegin(field);
      //  {
      //    oprot.WriteListBegin(new TList(TType.Struct, Success.Count));
      //    foreach (Supplier _iter3 in Success) {
      //      _iter3.Write(oprot);
      //    }
      //    oprot.WriteListEnd();
      //  }
      //  oprot.WriteFieldEnd();
      //}
      oprot.WriteFieldStop();
      oprot.WriteStructEnd();
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
