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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Andl.Runtime;

namespace Andl.Peg {
  /// <summary>
  /// Implement decoding generated code
  /// </summary>
  public class Decoder {
    public ByteCode Code { get; private set; }
    MemoryStream _mstream;
    PersistReader _preader;
    int _indent = 0;

    protected virtual void Dispose(bool disposing) {
      if (disposing) {
        _mstream.Dispose();
        _preader.Dispose();
      }
    }

    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    static public Decoder Create(ByteCode code) {
      var dc = new Decoder { 
        Code = code,
        _mstream = new MemoryStream(code.bytes)
      };
      dc._preader = PersistReader.Create(new BinaryReader(dc._mstream));
      return dc;
    }

    public void Decode() {
      Decode(_preader);
    }

    // Read a code stream and decode it
    void Decode(PersistReader preader) {
      while (preader.BaseStream.Position < preader.BaseStream.Length) {
        var opcode = preader.ReadOpcode();
        var prefix = String.Format(">{0}{1,4}: {2,-9}", new String(' ', _indent * 4), preader.BaseStream.Position - 1, opcode);
        //Logger.Write(3, ">{0}{1,4}: {2,-9}", new String(' ', _indent * 4), preader.BaseStream.Position - 1, opcode);
        switch (opcode) {
        // Known literal, do not translate into value
        case Opcodes.LDVALUE: //TODO: recurse
        case Opcodes.LDAGG:
          var value = preader.ReadValue();
          Logger.WriteLine(3, "{0}{1}", prefix, value);
          if (value.DataType == DataTypes.Code)
            Decode((value as CodeValue).Value.Code);
          break;
        case Opcodes.LDSEG: //TODO: recurse
          var code = preader.ReadExpr();
          Logger.WriteLine(3, "{0}{1}", prefix, code);
          Decode(code.Code);
          break;
        case Opcodes.LDCAT:
        case Opcodes.LDCATR:
        case Opcodes.LDFIELD:
        case Opcodes.LDCOMP:
        case Opcodes.LDFIELDT:
          Logger.WriteLine(3, "{0}{1}", prefix, preader.ReadString());
          break;
        case Opcodes.LDACC:
          Logger.WriteLine(3, "{0}{1} seed={2}", prefix, preader.ReadInteger(), preader.ReadValue());
          break;
        // Call a function, fixed or variable arg count
        case Opcodes.CALL:
        case Opcodes.CALLV:
        case Opcodes.CALLVT:
          Logger.WriteLine(3, "{0}{1} ({2}, {3})", prefix, preader.ReadString(), preader.ReadByte(), preader.ReadByte());
          break;
        case Opcodes.LDLOOKUP:
        case Opcodes.LDACCBLK:
          Logger.WriteLine(3, "{0}", prefix);
          break;
        default:
          throw new NotImplementedException(opcode.ToString());
        }
      }
    }

    void Decode(ByteCode code) {
      if (code.Length > 0) {
        _indent++;
        Decode(PersistReader.Create(code.bytes));
        _indent--;
      }
    }

  }
}
