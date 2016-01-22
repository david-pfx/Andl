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
using Andl.Runtime;

namespace Andl.Compiler {
  /// 
  /// Implement emitting generated code.
  /// 
  public class Emitter : IDisposable {
    MemoryStream _mstream;
    BinaryWriter _out;
    PersistWriter _pw;

    protected virtual void Dispose(bool disposing) {
      if (disposing) {
        _mstream.Dispose();
        _out.Dispose();
        _pw.Dispose();
      }
    }

    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    public Emitter() {
      _mstream = new MemoryStream();
      _out = new BinaryWriter(_mstream);
      _pw = PersistWriter.Create(_out);
    }

    public int GetMarker() {
      return (int)_mstream.Position;
    }

    // Take all code since marker, resetting the output position
    public ByteCode GetSeg(int marker, bool keep = false) {
      int curpos = (int)_mstream.Position;
      byte[] code = new Byte[curpos - marker];
      _mstream.Position = marker;
      _mstream.Read(code, 0, curpos - marker);
      _mstream.Position = (keep) ? curpos : marker;
      return new ByteCode { bytes = code };
    }

    // Take a copy of all the code without making any changes
    public ByteCode GetCode() {
      return new ByteCode { bytes = _mstream.ToArray() };
    }

    public void OutCall(Symbol symbol, int varargs = 0, CallInfo callinfo = null) {
      var ci = callinfo ?? symbol.CallInfo;
      switch (symbol.CallKind) {
      case CallKinds.FUNC:
        Logger.Assert(varargs == 0);
        OutCall(Opcodes.CALL, ci.Name, ci.NumArgs, 0);
        break;
      case CallKinds.VFUNC:
        OutCall(Opcodes.CALLV, ci.Name, ci.NumArgs, varargs);
        break;
      case CallKinds.VFUNCT:
        OutCall(Opcodes.CALLVT, ci.Name, ci.NumArgs, varargs);
        break;
      case CallKinds.JFUNC:
        OutLoad(NumberValue.Create((int)symbol.JoinOp));
        OutCall(Opcodes.CALL, ci.Name, ci.NumArgs, varargs);
        break;
      case CallKinds.LFUNC:
        Out(Opcodes.LDLOOKUP);
        OutCall(Opcodes.CALL, ci.Name, ci.NumArgs, varargs);
        break;
      default:
        Logger.Assert(false, symbol.CallKind);
        break;
      }
    }

    public void OutSegs(IEnumerable<ExpressionBlock> eblocks) {
      foreach (var eb in eblocks)
        OutSeg(eb);
    }

    public void OutSeg(ExpressionBlock eblock) {
      Out(Opcodes.LDSEG); 
      _pw.Write(eblock);
    }

    public void OutLoad(TypedValue value) {
      Out(Opcodes.LDVALUE);
      _pw.WriteValue(value);
    }

    // Output a symbol as its name
    public void OutName(Opcodes opcode, Symbol symbol) {
      Out(opcode);
      _out.Write(symbol.Name);
    }

    void OutCall(Opcodes opcode, string name, int fixargs, int varargs) {
      Out(opcode);
      _out.Write(name);
      _out.Write((byte)fixargs);
      _out.Write((byte)varargs);
    }

    public void Out(Opcodes opcode, TypedValue value) {
      Out(opcode);
      _pw.WriteValue(value);
    }

    public void Out(Opcodes opcode) {
      Logger.WriteLine(5, "{0,-4}: {1}", _mstream.Position, opcode);
      _out.Write((byte)opcode);
    }

    public void Out(ByteCode code) {
      _out.Write(code.bytes);
    }
  }
}
