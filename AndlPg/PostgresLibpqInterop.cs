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
using System.Text;

namespace Andl.PgClient {
  ///==============================================================================================
  /// <summary>
  /// Imports for Postres Libpq DLL
  /// 
  /// See: C:\Program Files\PostgreSQL\9.5\include\libpq-fe.h
  /// </summary>
  public class PostgresLibPqInterop {
    const string dllname = "libpq.dll";

    public enum ConnStatusType {
      CONNECTION_OK,
      CONNECTION_BAD,
      /* Non-blocking mode only below here */

      /*
       * The existence of these should never be relied upon - they should only
       * be used for user feedback or similar purposes.
       */
      CONNECTION_STARTED,           /* Waiting for connection to be made.  */
      CONNECTION_MADE,          /* Connection OK; waiting to send.     */
      CONNECTION_AWAITING_RESPONSE,     /* Waiting for a response from the
										 * postmaster.        */
      CONNECTION_AUTH_OK,           /* Received authentication; waiting for
								 * backend startup. */
      CONNECTION_SETENV,            /* Negotiating environment. */
      CONNECTION_SSL_STARTUP,       /* Negotiating SSL. */
      CONNECTION_NEEDED         /* Internal state: connect() needed */
    }

    public enum ExecStatusType {
      PGRES_EMPTY_QUERY = 0,		/* empty query string was executed */
      PGRES_COMMAND_OK,         /* a query command that doesn't return
								    * anything was executed properly by the
								    * backend */
      PGRES_TUPLES_OK,          /* a query command that returns tuples was
								    * executed properly by the backend, PGresult
								    * contains the result tuples */
      PGRES_COPY_OUT,               /* Copy Out data transfer in progress */
      PGRES_COPY_IN,                /* Copy In data transfer in progress */
      PGRES_BAD_RESPONSE,           /* an unexpected response was recv'd from the
								    * backend */
      PGRES_NONFATAL_ERROR,     /* notice or warning message */
      PGRES_FATAL_ERROR,            /* query failed */
      PGRES_COPY_BOTH,          /* Copy In/Out data transfer in progress */
      PGRES_SINGLE_TUPLE          /* single tuple from larger resultset */
    }

    //--- wrappers
    // http://stackoverflow.com/questions/12194828/c-sharp-callback-receiving-utf8-string
    public static string Utf8PtrToString(IntPtr utf8) {
      int len = MultiByteToWideChar(65001, 0, utf8, -1, null, 0);
      if (len == 0) throw new System.ComponentModel.Win32Exception();
      var buf = new StringBuilder(len);
      len = MultiByteToWideChar(65001, 0, utf8, -1, buf, len);
      return buf.ToString();
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int MultiByteToWideChar(int codepage, int flags, IntPtr utf8, int utf8len, StringBuilder buffer, int buflen);

    //--- callbacks
    //typedef void (*PQnoticeProcessor) (void* arg, const char* message);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void PQnoticeProcessorCallback(IntPtr arg, string message);

    //extern PQnoticeProcessor PQsetNoticeProcessor(PGconn* conn,
    //                 PQnoticeProcessor proc,
    //                 void* arg);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PQsetNoticeProcessor(IntPtr conn, PQnoticeProcessorCallback proc, IntPtr arg);

    //--- connection
    //extern PGconn* PQconnectdb(const char* conninfo);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PQconnectdb(string conninfo);

    //extern void PQfinish(PGconn *conn);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PQfinish(IntPtr conn);

    //extern ConnStatusType PQstatus(const PGconn* conn);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern ConnStatusType PQstatus(IntPtr conn);

    //extern ExecStatusType PQresultStatus(const PGresult *res);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern ExecStatusType PQresultStatus(IntPtr result);

    //--- execute
    //extern PGresult *PQexecParams(PGconn *conn,
    //const char *command,
    //int nParams,
    //const Oid *paramTypes,
    //const char *const * paramValues,
    //const int *paramLengths,
    //const int *paramFormats,
    //int resultFormat);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PQexecParams(IntPtr conn, string command,  int nParams,
      [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]int[]paramTypes,
      [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]string[] paramValues,
      [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]int[] paramLengths,
      [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]int[] paramFormats, int resultFormat);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PQexecParams(IntPtr conn, string command, int nParams, IntPtr paramTypes,
      [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]string[] paramValues,
      IntPtr paramLengths, IntPtr paramFormats, int resultFormat);

    //extern PGresult *PQgetResult(PGconn *conn);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PQgetResult(IntPtr conn);

    //extern char* PQerrorMessage(const PGconn* conn);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PQerrorMessage(IntPtr result);
    public static string PQerrorMessageW(IntPtr result) {
      return Utf8PtrToString(PQerrorMessage(result));
    }

    //extern char *PQresStatus(ExecStatusType status);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PQresStatus(ExecStatusType status);
    public static string PQresStatusW(ExecStatusType status) {
      return Utf8PtrToString(PQresStatus(status));
    }

    //extern char *PQresultErrorMessage(const PGresult *res);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PQresultErrorMessage(IntPtr result);
    public static string PQresultErrorMessageW(IntPtr result) {
      return Utf8PtrToString(PQresultErrorMessage(result));
    }

    //extern void PQclear(PGresult* res);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PQclear(IntPtr result);


    //extern char *PQresultErrorField(const PGresult *res, int fieldcode);
    //[DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    //public static extern IntPtr PQresultErrorField(IntPtr result, int fieldcode);

    //--- tuple values

    //extern int PQntuples(const PGresult* res);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PQntuples(IntPtr result);

    //extern int PQnfields(const PGresult* res);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PQnfields(IntPtr result);

    //extern int PQfformat(const PGresult* res, int field_num);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PQfformat(IntPtr result, int field_num);

    //extern char* PQgetvalue(const PGresult* res, int tup_num, int field_num);
    [DllImport(dllname, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PQgetvalue(IntPtr result, int tup_num, int field_num);
    public static string PQgetvalueW(IntPtr result, int tup_num, int field_num) {
      return Utf8PtrToString(PQgetvalue(result, tup_num, field_num));
    }

  }
}
