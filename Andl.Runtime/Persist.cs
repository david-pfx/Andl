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

namespace Andl.Runtime {
  /// <summary>
  /// Implement persistence of all kinds of variables.
  /// 
  /// For database, writes out everything in the catalog database scope level
  /// Reads in everything from the named directory.
  /// 
  /// Also used for persisting compiled code.
  /// 
  /// </summary>
  public class Persist {
    public const string Signature = "Andl 1.1";
    public const string VariableExtension = "vandl";
    public const int RelationSignature = 0x7b7b;
    public const int TupleSignature = 0x7b;
    public const int UserSignature = 0x3a;
    public const int MaxDegree = 256;
    string _basepath;

    // Create a persister, in memory or to a path
    public static Persist Create(string basepath, bool cancreate) {
      if (!Directory.Exists(basepath)) 
        if (!cancreate) RuntimeError.Fatal("Persist", "database does not exist: " + basepath);
      Directory.CreateDirectory(basepath);
      return new Persist { _basepath = basepath };
    }

    public static Persist Create() {
      return new Persist();
    }

    public PersistWriter Writer(string name) {
      var path = Path.Combine(_basepath, name + "." + VariableExtension);
      var bw = new BinaryWriter(File.Open(path, FileMode.Create));
      return PersistWriter.Create(bw);
    }

    public PersistReader Reader(string name) {
      var path = Path.Combine(_basepath, name + "." + VariableExtension);
      if (!File.Exists(path)) return null;
      var reader = new BinaryReader(File.Open(path, FileMode.Open));
      return PersistReader.Create(reader);
    }

    // Store typed value on file stream
    public void Store(string name, TypedValue value) {
      var path = Path.Combine(_basepath, name + "." + VariableExtension);
      Logger.WriteLine(3, "Storing {0} type={1}", name, value.DataType.ToString());
      try {
        using (var writer = new BinaryWriter(File.Open(path, FileMode.Create))) {
          var w = PersistWriter.Create(writer);
          w.Store(value);
        }
      } catch (Exception) {
        RuntimeError.Fatal("Persist Store", "storing {0}", name);
      }
    }

    // Store typed value on memory stream, return byte array
    public byte[] Store(TypedValue value) {
      using (var writer = new BinaryWriter(new MemoryStream())) {
        var w = PersistWriter.Create(writer);
        w.Store(value);
        return (writer.BaseStream as MemoryStream).ToArray();
      }
    }

    // Load from file stream
    public TypedValue Load(string name) {
      var path = Path.Combine(_basepath, name + "." + VariableExtension);
      Logger.WriteLine(2, "Loading {0}", name);
      if (!File.Exists(path)) return TypedValue.Empty;
      using (var reader = new BinaryReader(File.Open(path, FileMode.Open))) {
        var r = PersistReader.Create(reader);
        return r.Load();
      }
    }

    // Peek file stream to get type
    public DataType Peek(string name) {
      var path = Path.Combine(_basepath, name + "." + VariableExtension);
      if (!File.Exists(path)) return null;
      Logger.WriteLine(2, "Peeking {0}", name);
      using (var reader = new BinaryReader(File.Open(path, FileMode.Open))) {
        var r = PersistReader.Create(reader);
        return r.Peek();
      }
    }

    // Load from byte array
    public TypedValue Load(byte[] buffer) {
      Logger.WriteLine(2, "Loading data");
      using (var reader = new BinaryReader(new MemoryStream(buffer))) {
        var r = PersistReader.Create(reader);
        return r.Load();
      }
    }
  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Implement writing a value to a persistence stream or store
  /// </summary>
  public class PersistWriter : IDisposable {
    BinaryWriter _writer;

    protected virtual void Dispose(bool disposing) {
      if (disposing) _writer.Dispose();
    }

    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    public Byte[] ToArray() {
      return (_writer.BaseStream as MemoryStream).ToArray();
    }

    public static PersistWriter Create() {
      return PersistWriter.Create(new BinaryWriter(new MemoryStream()));
    }

    public static PersistWriter Create(BinaryWriter writer) {
      return new PersistWriter {
        _writer = writer
      };
    }

    public static PersistWriter Create(string path) {
      return new PersistWriter {
        _writer = new BinaryWriter(File.OpenWrite(path)),
      };
    }

    // shorthand to serialise a value of known type
    // must include type to know column order
    public static byte[] ToBinary(TypedValue value) {
      using (var writer = PersistWriter.Create()) {
        writer.WriteValue(value);
        return writer.ToArray();
      }
    }

    // shorthand to serialise a heading
    public static byte[] ToBinary(DataHeading heading) {
      using (var writer = PersistWriter.Create()) {
        writer.Write(heading);
        return writer.ToArray();
      }
    }

    // Store the value of a variable as an individual store, with signature
    // The type is known completely in advance
    public void Store(TypedValue value) {
      _writer.Write(Persist.Signature);
      WriteValue(value);
      _writer.Write(Persist.Signature);
    }

    // === Functions to write recursively all possible elements ===

    // Write a typed value with its type - also called by emitter
    // note that the heading from the value preserves column order, which type may not
    public void WriteValue(TypedValue value) {
      Write(value.DataType, value.Heading);
      // call specific routine (which will be generic for relation, tuple and user)
      Write(value);
    }

    // Write a typed value bare
    // Works by dispatching to  specific routine (which will be generic for relation, tuple and user)
    // Note: in general a call to this function does not write out any type information. The
    // exceptions are ExpressionBlock and AccumulatorBlock.
    public void Write(TypedValue value) {
      Logger.Assert(_writerdict.ContainsKey(value.DataType.BaseType), value.DataType.BaseType);
      _writerdict[value.DataType.BaseType](this, value);
    }

    // write an accumulator block
    public void Write(AccumulatorBlock accum) {
      Write((byte)accum.IndexBase);
      WriteValue(accum.Result);
      Write((byte)accum.Accumulators.Length);
      for (int i = 0; i < accum.Accumulators.Length; ++i)
        WriteValue(accum.Accumulators[i]);
    }

    // write a code value
    public void Write(ExpressionBlock eblock) {
      Write(eblock.Name);
      Write((byte)eblock.Kind);
      Write(eblock.Serial);
      switch (eblock.Kind) {
      case ExpressionKinds.Project:
        Write(eblock.DataType);
        break;
      case ExpressionKinds.Rename:
        Write(eblock.OldName);
        Write(eblock.DataType);
        break;
      case ExpressionKinds.Order:
        Write(eblock.DataType);
        Write(eblock.IsGrouped);
        Write(eblock.IsDesc);
        break;
      case ExpressionKinds.Value:
        WriteValue(eblock.Value);
        break;
      default:
        Write(eblock.Code);
        Write(eblock.DataType);
        Write(eblock.AccumCount);
        WriteArgType(eblock.Lookup);
        Write(eblock.IsLazy);
        break;
      }
    }

    //-- implementation

    // function to write out typed value
    Dictionary<DataType, Action<PersistWriter, TypedValue>> _writerdict = new Dictionary<DataType, Action<PersistWriter, TypedValue>>() {
      { DataTypes.Binary, (pw, v) => {  pw._writer.Write(((BinaryValue)v).Value.Length); 
                                        pw._writer.Write(((BinaryValue)v).Value);  } },
      { DataTypes.Bool,   (pw, v) => pw._writer.Write(((BoolValue)v).Value) },
      { DataTypes.Code,   (pw, v) => pw.Write((v as CodeValue).Value) },
      { DataTypes.Heading,(pw, v) => pw.Write(v.AsHeading()) },
      { DataTypes.Number, (pw, v) => pw._writer.Write(((NumberValue)v).Value) },      // TODO: platform
      { DataTypes.Row,    (pw, v) => pw.Write(v.AsRow()) },
      { DataTypes.Table,  (pw, v) => pw.Write(v.AsTable()) },
      { DataTypes.Text,   (pw, v) => pw._writer.Write(((TextValue)v).Value) },
      { DataTypes.Time,   (pw, v) => pw._writer.Write(((TimeValue)v).Value.Ticks) },  // TODO: platform
      { DataTypes.User,   (pw, v) => {  //pw._writer.Write(Persist.UserSignature);
                                        pw.Write(v.AsUser()); } },

    };

    void Write(ByteCode code) {
      Write(code.Length);
      if (code.Length > 0)
        _writer.Write(code.bytes);
    }

    void WriteArgType(DataHeading argtype) {
      var numargs = argtype == null ? 0 : argtype.Degree;
      Write((byte)numargs);
      if (numargs > 0)
        Write(argtype);
    }

    // write out a data type
    // if column order matters, a heading is required
    public void Write(DataType datatype, DataHeading heading = null) {
      Write(datatype.BaseType.Name);
      if (datatype is DataTypeUser)
        Write(datatype.Name);
      // Type requires a heading. If supplied use that, because it
      // will be column order preserving
      if (datatype.BaseType.HasHeading)
        Write(heading ?? datatype.Heading);
    }

    void Write(DataHeading heading) {
      Write(heading.Degree);
      foreach (var col in heading.Columns)
        Write(col);
    }

    void Write(DataColumn column) {
      Write(column.Name);
      Write(column.DataType); //BUG: column does not remember its heading. Hopefully it matches type.
    }

    void Write(DataTable table) {
      var tbl = DataTableLocal.Convert(table);
      //Write(Persist.RelationSignature);
      Write(tbl.Cardinality);
      foreach (var row in tbl.GetRows())
        Write(row);
    }

    void Write(DataRow row) {
      //Write(Persist.TupleSignature);
      Write(row.Values);
    }

    // Tuple or User value is just an ordered sequence of typed values
    void Write(TypedValue[] values) {
      foreach (var v in values)
        Write(v);
    }

    // write base types
    public void Write(string value) {
      _writer.Write(value);
    }
    void Write(int value) {
      _writer.Write(value);
    }
    internal void Write(byte value) {
      _writer.Write(value);
    }
    void Write(bool value) {
      _writer.Write(value);
    }
  }

  //////////////////////////////////////////////////////////////////////
  /// <summary>
  /// Implement reading a value from a persistence stream or store
  /// </summary>
  public class PersistReader : IDisposable {
    //--- publics
    public Stream BaseStream { get { return _reader.BaseStream; } }
    public bool More { get { return BaseStream.Position < BaseStream.Length; } }

    BinaryReader _reader;

    public static PersistReader Create(BinaryReader reader) {
      return new PersistReader { _reader = reader };
    }

    public static PersistReader Create(byte[] buffer) {
      return new PersistReader { _reader = new BinaryReader(new MemoryStream(buffer)) };
    }

    public static PersistReader Create(string path) {
      return new PersistReader { _reader = new BinaryReader(File.OpenRead(path)) };
    }

    // Serialise reading a value
    // must include type to know column order :BUG
    public static TypedValue FromBinary(byte[] buffer, DataType datatype) {
      using (var reader = PersistReader.Create(buffer)) {
        var value = reader.ReadValue();
        Logger.Assert(value.DataType.Equals(datatype), value);
        return value;
      }
    }

    // Load the value of a variable from the database
    // Complements Store
    public TypedValue Load() {
      if (_reader.ReadString() != Persist.Signature) RuntimeError.Fatal("Load catalog", "invalid signature");
      var value = ReadValue();
      if (_reader.ReadString() != Persist.Signature) RuntimeError.Fatal("Load catalog", "invalid signature");
      return value;
    }

    // Peek the type of a variable in the database
    public DataType Peek() {
      //Logger.Assert(_reader.ReadString() == Persist.Signature);
      var type = ReadDataType();
      return type;
    }

    // Read a value -- type not yet known
    // Must read both type and heading to preserve column order
    public TypedValue ReadValue() {
      var basetype = ReadBaseType();
      var username = (basetype == DataTypes.User) ? ReadString() : null;
      var heading = (basetype.HasHeading) ? ReadHeading() : null;
      return Read(DataType.Derive(basetype, heading, username), heading);
    }

    // Read a value -- type already known
    // Must provide heading to preserve column order
    public TypedValue Read(DataType datatype, DataHeading heading = null) {
      Logger.Assert(_readdict.ContainsKey(datatype.BaseType), datatype);
      return _readdict[datatype.BaseType](this, datatype, heading);
    }

    // --- functions to read each kind of data from the reader
    // functions to create typed value from column value of given type
    // composite types need heading (for column order); user needs datatype (for name)
    static Dictionary<DataType, Func<PersistReader, DataType, DataHeading, TypedValue>> _readdict = new Dictionary<DataType, Func<PersistReader, DataType, DataHeading, TypedValue>>() {
      { DataTypes.Bool,   (pr, dt, dh) => BoolValue.Create(pr._reader.ReadBoolean()) },
      { DataTypes.Number, (pr, dt, dh) => NumberValue.Create(pr._reader.ReadDecimal()) },
      { DataTypes.Time,   (pr, dt, dh) => TimeValue.Create(new DateTime(pr._reader.ReadInt64())) },
      { DataTypes.Text,   (pr, dt, dh) => TextValue.Create(pr._reader.ReadString()) },
      { DataTypes.Binary, (pr, dt, dh) => { var length = pr._reader.ReadInt32(); 
                                        var bytes = pr._reader.ReadBytes(length);
                                        return BinaryValue.Create(bytes); } },
      { DataTypes.Code,   (pr, dt, dh) => CodeValue.Create(pr.ReadExpr()) },
      { DataTypes.Heading,(pr, dt, dh) => HeadingValue.Create(pr.ReadHeading()) },
      { DataTypes.Table,  (pr, dt, dh) => TypedValue.Create(pr.ReadTable(dh)) },
      { DataTypes.Row,    (pr, dt, dh) => TypedValue.Create(pr.ReadRow(dh)) },
      { DataTypes.User,   (pr, dt, dh) => UserValue.Create(pr.ReadUser(dh), dt as DataTypeUser) },
    };

    // read an accumulator block
    public AccumulatorBlock ReadAccum() {
      var ibase = ReadByte();
      var result = ReadValue();
      var naccum = ReadByte();
      var accum = AccumulatorBlock.Create(naccum);
      accum.IndexBase = ibase;
      accum.Result = result;
      for (int i = 0; i < naccum; ++i)
        accum.Accumulators[i] = ReadValue();
      return accum;
    }

    // read an expression block
    public ExpressionBlock ReadExpr() {
      ExpressionBlock eb;
      var name = ReadString();
      var kind = (ExpressionKinds)ReadByte();
      var serial = ReadInteger();
      switch (kind) {
      case ExpressionKinds.Project:
        eb = ExpressionBlock.Create(name, name, ReadDataType());
        break;
      case ExpressionKinds.Rename:
        eb = ExpressionBlock.Create(name, ReadString(), ReadDataType());
        break;
      case ExpressionKinds.Order:
        eb = ExpressionBlock.Create(name, ReadDataType(), _reader.ReadBoolean(), _reader.ReadBoolean());
        break;
      case ExpressionKinds.Value:
        eb = ExpressionBlock.Create(name, ReadValue());
        break;
      default:
        eb = ExpressionBlock.Create(name, kind, ReadByteCode(), ReadDataType(), ReadInteger(), ReadArgType(), _reader.ReadBoolean(), serial);
        break;
      }
      return eb;
    }

    // read a length of byte code
    ByteCode ReadByteCode() {
      var codelen = ReadInteger();
      return new ByteCode { bytes = (codelen > 0) ? _reader.ReadBytes(codelen) : null };
    }

    // read a function signature
    DataHeading ReadArgType() {
      var numargs = ReadByte();
      return (numargs > 0) ? ReadHeading() : null;
    }

    // read a full data type
    // note: final type may not preserve column order
    public DataType ReadDataType() {
      var basetype = ReadBaseType();
      var username = (basetype.HasName) ? ReadString() : null;
      var heading = (basetype.HasHeading) ? ReadHeading() : null;
      return DataType.Derive(basetype, heading, username);
    }

    // read the base part of a data type
    DataType ReadBaseType() {
      return DataTypes.Find(_reader.ReadString());
    }

    // read a heading
    public DataHeading ReadHeading() {
      var degree = _reader.ReadInt32();
      Logger.Assert(degree >= 0 && degree < Persist.MaxDegree, degree);
      var cols = new List<DataColumn>();
      while (degree-- > 0)
        cols.Add(ReadColumn());
      return DataHeading.Create(cols.ToArray());
    }

    // read a column
    DataColumn ReadColumn() {
      var name = _reader.ReadString();
      var datatype = ReadDataType();
      return DataColumn.Create(name, datatype);
    }

    // note that the heading implies the order of values -- which is critical!

    // read a table -- heading already known
    DataTable ReadTable(DataHeading heading) {
      //Logger.Assert(_reader.ReadInt32() == Persist.RelationSignature);
      var table = DataTableLocal.Create(heading);
      var cardinality = _reader.ReadInt32();
      while (cardinality-- > 0)
        table.AddRow(ReadRow(heading));
      return table;
    }

    // read a row -- heading already known
    DataRow ReadRow(DataHeading heading) {
      //Logger.Assert(_reader.ReadInt32() == Persist.TupleSignature);
      var values = new TypedValue[heading.Degree];
      for (int x = 0; x < heading.Degree; ++x) {
        var datatype = heading.Columns[x].DataType;
        values[x] = Read(datatype, datatype.Heading); // NOTE: this heading could be out of order
      }
      return DataRow.Create(heading, values);
    }

    // read a user defined type, already known
    TypedValue[] ReadUser(DataHeading heading) {
      //Logger.Assert(_reader.ReadInt32() == Persist.UserSignature);
      var values = new TypedValue[heading.Degree];
      for (var i = 0; i < heading.Degree; ++i)
        values[i] = Read(heading.Columns[i].DataType);
      return values;
    }

    // read an opcode
    public Opcodes ReadOpcode() {
      return (Opcodes)ReadByte();
    }

    // read base types
    public string ReadString() {
      return _reader.ReadString();
    }
    public int ReadInteger() {
      return _reader.ReadInt32();
    }
    public int ReadByte() {
      return _reader.ReadByte();
    }
    public bool ReadBool() {
      return _reader.ReadBoolean();
    }

    //--- IDispose

    protected virtual void Dispose(bool disposing) {
      if (disposing) _reader.Dispose();
    }

    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

  }

}
