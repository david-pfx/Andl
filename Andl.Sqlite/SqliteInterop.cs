using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Andl.Sqlite {
  /// <summary>
  /// Implements a basic unadorned access to the Sqlite API.
  /// 
  /// Note custom marshalling for functions that return string. If declared directly, P/Invoke will try to deallocate the string.
  /// </summary>
  public class SqliteInterop {
    public const int TableInfoNameColumn = 1;
    public const int TableInfoTypeColumn = 2;
    public const int TableInfoColumnCount = 6;

    public enum Access : int {
      EXISTS = 0,
      READWRITE = 1,
      READ = 2
    }
    public enum Result : int {
      OK = 0,
      ERROR = 1,
      INTERNAL = 2,
      PERM = 3,
      ABORT = 4,
      BUSY = 5,
      LOCKED = 6,
      NOMEM = 7,
      READONLY = 8,
      INTERRUPT = 9,
      IOERR = 10,
      CORRUPT = 11,
      NOTFOUND = 12,
      FULL = 13,
      CANTOPEN = 14,
      PROTOCOL = 15,
      EMPTY = 16,
      SCHEMA = 17,
      TOOBIG = 18,
      CONSTRAINT = 19,
      MISMATCH = 20,
      MISUSE = 21,
      NOLFS = 22,
      AUTH = 23,
      FORMAT = 24,
      RANGE = 25,
      NOTADB = 26,
      ROW = 100,
      DONE = 101
    }

    // Fundamental data types
    public enum Datatype : int {
      INTEGER = 1,
      FLOAT = 2,  // = REAL
      TEXT = 3,
      BLOB = 4,
      NULL = 5
    }

    // Text Encodings
    public enum Encoding : int {
      UTF8           = 1,
      UTF16LE        = 2,
      UTF16BE        = 3,
      UTF16          = 4, // native
      ANY            = 5, // deprecated
      UTF16_ALIGNED  = 8,
    };

    // special values for last argument of sqlite3_result_text
    public static readonly IntPtr SQLITE_STATIC = (IntPtr)0;
    public static readonly IntPtr SQLITE_TRANSIENT = (IntPtr)(-1);

    ///-------------------------------------------------------------------------------------------- 
    /// wrappers to handle marshalling, avoid nulls, do conversions
    /// 

    public static string sqlite3_errmsg_wrapper(IntPtr db) {
      return Marshal.PtrToStringAnsi(sqlite3_errmsg_raw(db));
    }

    public static byte[] sqlite3_column_blob_wrapper(IntPtr pstmt, int iCol) {
      var len = sqlite3_column_bytes(pstmt, iCol);
      var ptr = sqlite3_column_blob(pstmt, iCol);
      var ret = new byte[len];
      if (ptr == IntPtr.Zero) return ret;
      Marshal.Copy(ptr, ret, 0, len);
      return ret;
    }

    public static object sqlite3_value_blob_wrapper(IntPtr pvalue) {
      var len = sqlite3_value_bytes(pvalue);
      var ptr = sqlite3_value_blob(pvalue);
      var ret = new byte[len];
      if (ptr == IntPtr.Zero) return ret;
      Marshal.Copy(ptr, ret, 0, len);
      return ret;
    }

    // get column text
    public static string sqlite3_column_text_wrapper(IntPtr pstmt, int iCol) {
      var ptr = sqlite3_column_text(pstmt, iCol);
      if (ptr == IntPtr.Zero) return "";
      else return Marshal.PtrToStringAnsi(ptr);
    }

    public static string sqlite3_column_text16_wrapper(IntPtr pstmt, int iCol) {
      var ptr = sqlite3_column_text16(pstmt, iCol);
      if (ptr == IntPtr.Zero) return "";
      else return Marshal.PtrToStringUni(ptr);
    }

    public static string sqlite3_value_text_wrapper(IntPtr pvalue) {
      return Marshal.PtrToStringAnsi(sqlite3_value_text(pvalue));
    }

    public static string sqlite3_value_text16_wrapper(IntPtr pvalue) {
      return Marshal.PtrToStringUni(sqlite3_value_text16(pvalue));
    }

    ///--------------------------------------------------------------------------------------------
    ///
    /// Declarations and Entry points
    /// 

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void UserFunctionCallback(IntPtr context, int nvalues,
      [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)]IntPtr[] values);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void UserFunctionStepCallback(IntPtr context, int nvalues,
      [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)]IntPtr[] values);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void UserFunctionFinalCallback(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void UserFunctionDestructorCallback(IntPtr context);

    [DllImport("sqlite3.dll", EntryPoint = "sqlite3_open", CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_open(string filename, out IntPtr db);

    [DllImport("sqlite3.dll", EntryPoint = "sqlite3_close", CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_close(IntPtr db);

    [DllImport("sqlite3.dll", EntryPoint = "sqlite3_errmsg", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr sqlite3_errmsg_raw(IntPtr db);

    [DllImport("sqlite3.dll", EntryPoint = "sqlite3_create_function", CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_create_function_scalar(IntPtr db, string zFunctionName, int nArg, int eTextRep, IntPtr pApp,
      UserFunctionCallback xFunc, IntPtr xStep, IntPtr xFinal);

    [DllImport("sqlite3.dll", EntryPoint = "sqlite3_create_function", CallingConvention = CallingConvention.Cdecl)]
    public static extern int sqlite3_create_function_aggregate(IntPtr db, string zFunctionName, int nArg, int eTextRep, IntPtr pApp,
      IntPtr xFunc, UserFunctionStepCallback xStep, UserFunctionFinalCallback xFinal);

[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_column_count(IntPtr pstmt);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern string sqlite3_column_origin_name(IntPtr pstmt, int iCol);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_column_blob(IntPtr pstmt, int iCol);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_column_bytes(IntPtr pstmt, int iCol);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern double sqlite3_column_double(IntPtr pstmt, int iCol);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_column_int(IntPtr pstmt, int iCol);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern Int64 sqlite3_column_int64(IntPtr pstmt, int iCol);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_column_text(IntPtr pstmt, int iCol);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
                                                                        public static extern IntPtr sqlite3_column_text16(IntPtr pstmt, int iCol);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_column_type(IntPtr pstmt, int iCol);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_column_value(IntPtr pstmt, int iCol);

[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_aggregate_context(IntPtr pcontext, int nBytes);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void sqlite3_result_blob(IntPtr pcontext, byte[] pdata, int length, IntPtr dtor);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void sqlite3_result_double(IntPtr pcontext, double value);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void sqlite3_result_error(IntPtr pcontext, string message, int length);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void sqlite3_result_error_toobig(IntPtr pcontext);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void sqlite3_result_error_nomem(IntPtr pcontext);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void sqlite3_result_error_code(IntPtr pcontext, int code);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void sqlite3_result_int(IntPtr pcontext, int value);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void sqlite3_result_int64(IntPtr pcontext, Int64 value);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void sqlite3_result_null(IntPtr pcontext);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void sqlite3_result_text(IntPtr pcontext, string value, int length, IntPtr dtor);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
                                                                        public static extern void sqlite3_result_text16(IntPtr pcontext, string value, int length, IntPtr dtor);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void sqlite3_result_value(IntPtr pcontext, IntPtr pvalue);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void sqlite3_result_zeroblob(IntPtr pcontext, int n);

    // prepared statements
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_prepare_v2(IntPtr db, string zSql, int nByte, out IntPtr ppStmpt, IntPtr pzTail);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_step(IntPtr pstmt);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_finalize(IntPtr pstmt);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_reset(IntPtr pStmt);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_clear_bindings(IntPtr pStmt);

    // bindings
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_blob(IntPtr pstmt, int index, byte[] pdata, int n, IntPtr dtor);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_double(IntPtr pstmt, int index, double value);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_int(IntPtr pstmt, int index, int value);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_int64(IntPtr pstmt, int index, Int64 value);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_null(IntPtr pstmt, int index);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_text(IntPtr pstmt,int index, string value, int len, IntPtr dtor);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
                                                                        public static extern int sqlite3_bind_text16(IntPtr pstmt,int index, string value, int len, IntPtr dtor);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_value(IntPtr pstmt, int index, IntPtr pvalue);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_bind_zeroblob(IntPtr pstmt, int index, int n);

    // Value entry points, used inside user function
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_value_blob(IntPtr pvalue);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_value_bytes(IntPtr pvalue);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_value_bytes16(IntPtr pvalue);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern double sqlite3_value_double(IntPtr pvalue);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_value_int(IntPtr pvalue);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern Int64 sqlite3_value_int64(IntPtr pvalue);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_value_text(IntPtr pvalue);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
                                                                        public static extern IntPtr sqlite3_value_text16(IntPtr pvalue);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_value_type(IntPtr pvalue);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern int sqlite3_value_numeric_type(IntPtr pvalue);

[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_malloc(int bytes);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr sqlite3_realloc(IntPtr ptr, int bytes);
[DllImport("sqlite3.dll", CallingConvention = CallingConvention.Cdecl)] public static extern void sqlite3_free(IntPtr ptr);

  }
}
