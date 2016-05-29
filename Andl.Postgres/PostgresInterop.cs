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
using System.Runtime.InteropServices;

namespace Andl.Postgres {
  ///==============================================================================================
  /// <summary>
  /// Imports for Postres language handler DLL (cannot access Postgres directly)
  /// </summary>
  public class PostgresInterop {
    const string dllname = "plandl.dll";

    public enum SpiReturn {
      OK = 0,
      MISUSE = -29,
      CONVERSION = -28,
      SPI_ERROR_CONNECT = -1,
      SPI_ERROR_COPY = -2,
      SPI_ERROR_OPUNKNOWN = -3,
      SPI_ERROR_UNCONNECTED = -4,
      SPI_ERROR_ARGUMENT = -6,
      SPI_ERROR_PARAM = -7,
      SPI_ERROR_TRANSACTION = -8,
      SPI_ERROR_NOATTRIBUTE = -9,
      SPI_ERROR_NOOUTFUNC = -10,
      SPI_ERROR_TYPUNKNOWN = -11,
      SPI_OK_CONNECT = 1,
      SPI_OK_FINISH = 2,
      SPI_OK_FETCH = 3,
      SPI_OK_UTILITY = 4,
      SPI_OK_SELECT = 5,
      SPI_OK_SELINTO = 6,
      SPI_OK_INSERT = 7,
      SPI_OK_DELETE = 8,
      SPI_OK_UPDATE = 9,
      SPI_OK_CURSOR = 10,
      SPI_OK_INSERT_RETURNING = 11,
      SPI_OK_DELETE_RETURNING = 12,
      SPI_OK_UPDATE_RETURNING = 13,
      SPI_OK_REWRITTEN = 14,
    }

    // Type OIDs suitable for conversions
    public enum TypeOid {
      BOOLOID = 16,
      BYTEAOID = 17,
      INT4OID = 23,
      TEXTOID = 25,
      FLOAT8OID = 701,
      NUMERICOID = 1700,
      TIMESTAMPOID = 1114,
    }

    // Error codes used when calling elog()
    public enum ElogLevel {
      DEBUG5 = 10,
      DEBUG4 = 11,
      DEBUG3 = 12,
      DEBUG2 = 13,
      DEBUG1 = 14,
      LOG = 15,
      COMMERROR	= 16,
      INFO = 17,
      NOTICE = 18,
      WARNING = 19,
      ERROR = 20,
      FATAL = 21,
      PANIC = 22,
    }	       

    //--- callbacks
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int ConnectCallback(int handle, string options);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int TypeCheckCallback(int handle, string funcname, int nargs,
    [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]int[] argtypes, int rettype);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int InvokeCallback(int handle, string funcname, int nargs,
    [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]IntPtr[] arguments, IntPtr retval);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr GetMessageCallback(int handle);

    //--- plandl imports
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern int plandl_init_callback(ConnectCallback cncb, TypeCheckCallback tccb, InvokeCallback ivcb, GetMessageCallback gmcb);

    ///----- pass through to PG -----------------------------------------------
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pg_alloc_mem(int len);

    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pg_realloc_mem(IntPtr ptr, int len);

    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pg_alloc_datum(int len);

    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pg_cstring_to_numeric(string value);

    // returns string, but marshaller will free memory...
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pg_numeric_to_cstring(IntPtr value);

    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pg_cstring_to_timestamp(string value);

    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pg_timestamp_to_cstring(IntPtr value);

    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pg_detoast_bytea(IntPtr value);

    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern void pg_elog(ElogLevel elevel, string message);

    ///----- SPI --------------------------------------------------------------
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pg_spi_connect();

    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pg_spi_finish();

    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pg_spi_execute(string sql, bool read_only);

    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pg_spi_execute_plan(IntPtr plan, int nvalues, 
      [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)]IntPtr[] values, bool read_only);

    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pg_spi_prepare_cursor(string sql, int nargs, 
      [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)]int[] argtypes, int options, out IntPtr plan);

    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pg_spi_getdatum(int row, int column, out IntPtr datum);

    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pg_spi_cursor_execute(string sql, bool read_only, out IntPtr portal);

    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern int pg_spi_cursor_fetch(IntPtr portal);
  }
}
