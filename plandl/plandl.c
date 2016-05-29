// plandl -- andl language handler for Postgres

/* 
  See: http://blog.2ndquadrant.com/compiling-postgresql-extensions-visual-studio-windows/

  This build is for x64 only

  1. This file extension is .c (cpp will not work, and triggers compile error)
     Must compile as C code, and disable C exceptions.
  2. Note the need for PGDLLEXPORT prototype so that function gets exported.
  3. Set WIN32 def -- not sure if it's still needed, but no harm
  4. Settings: includes, libs.
  5. Requires to be a host for CLR: implemented in C++ COM. /clr not required.
    
*/

#include "postgres.h"
#include "executor/spi.h"
#include "commands/trigger.h"
#include "fmgr.h"
#include "access/heapam.h"
#include "access/htup_details.h"
#include "utils/syscache.h"
#include "catalog/pg_proc.h"
#include "catalog/pg_type.h"

#include "utils/acl.h"
#include "utils/builtins.h"
#include "utils/timestamp.h"

// calls to external C++ to set up CLR hosting
extern int load_clr(char* path);
extern int lasterr();
extern char* lastmsg();

// called by PG
PGDLLEXPORT void _PG_init();
PGDLLEXPORT Datum plandl_call_handler(PG_FUNCTION_ARGS);

// called by P/Invoke to set up callbacks
PGDLLEXPORT int plandl_init_callback(void* connfn, void* checkfn, void* evalfn, void* gmfn);

// called by P/Invoke and passed through to PG
PGDLLEXPORT void* pg_alloc_copy(void* ptr, int len);
PGDLLEXPORT void* pg_alloc_mem(int len);
PGDLLEXPORT void* pg_realloc_mem(void* ptr, int len);
PGDLLEXPORT void* pg_alloc_datum(int len);
PGDLLEXPORT Datum pg_cstring_to_numeric(char* value);
PGDLLEXPORT char* pg_numeric_to_cstring(Datum value);
PGDLLEXPORT Datum pg_cstring_to_timestamp(char* value);
PGDLLEXPORT char* pg_timestamp_to_cstring(Datum value);
PGDLLEXPORT Datum pg_detoast_bytea(Datum value);
PGDLLEXPORT void pg_elog(int elevel, char* message);

PGDLLEXPORT int pg_spi_connect(void);
PGDLLEXPORT int	pg_spi_finish(void);
PGDLLEXPORT int	pg_spi_execute(const char *sql, bool read_only);
PGDLLEXPORT int pg_spi_execute_plan(SPIPlanPtr plan, int nvalues, Datum *values, bool read_only);
PGDLLEXPORT int pg_spi_prepare_cursor(const char *sql, int nargs, Oid *argtypes, int options, SPIPlanPtr* plan);
PGDLLEXPORT int pg_spi_getdatum(int row, int column, Datum* datum);
PGDLLEXPORT int pg_spi_cursor_execute(const char* sql, bool read_only, Portal* portal);
PGDLLEXPORT int pg_spi_cursor_fetch(Portal portal);

// special return codes
#define SPI_OK 0

// magic data item
PG_MODULE_MAGIC;
PG_FUNCTION_INFO_V1(plandl_call_handler);

#define MYNAME "plandl_call_handler"
// initialisation relies on a function of this name with content of: '<path>|options'
#define INIT_NAME "plandl_compile"
#define OPTION_DELIM '|' // cannot occur in filename

int init = 0;			// counter to detect duplication initialisation
bool init_ok = false;	// true once connected to runtime
int myinst = 17;		// magic number for instance -- may not be needed
bool isnoisy = false;	// controls debug output from this module only

typedef int (connfn)(int handle, char* options);
typedef int (checkfn)(int handle, char* name, int nargs, Oid* argtyps, Oid rettyp);
typedef int (invokefn)(int handle, char* name, int nargs, Datum* args, Datum* retval);
typedef char* (getmfn)(int handle);
connfn* pfn_connect = 0;
checkfn* pfn_typecheck = 0;
invokefn* pfn_invoke = 0;
getmfn* pfn_getmessage = 0;

//-----------------------------------------------------------------------------
// Postgres initialisation -- called once
//
// called by Postgres when DLL is loaded, but before setting up anything useful
void _PG_init() {
	++init;
	// nothing to do here
}

// called by language handler as special to do initialisation
// path for assembly is required parameter
void handler_init(char* path, char* options) {
	// 3 times: PG_init, call_handler, and this one
	++init;
	// previous initialisation may have failed without our knowledge
	if (init != 3) elog(ERROR, "Previous load failed (%d), new session required: hr=%x msg=%s", init, lasterr(), lastmsg());

	// load the CLR and call runtime entrypoint
	if (!load_clr(path)) elog(ERROR, "Load runtime failed: hr=%x msg=%s", lasterr(), lastmsg());

	// initialisation can fail in Postgres and will not return
	if (!pfn_connect(myinst, options)) elog(ERROR, "Connect to gateway failed: %s", pfn_getmessage(myinst));

	// all ok
	if (isnoisy) elog(NOTICE, "=== Andl_init OK %x %s", lasterr(), lastmsg());
	init_ok = true;
}
//-----------------------------------------------------------------------------
// Managed code will call back here, save pointers for later
//
int plandl_init_callback(void* connfn, void* checkfn, void* invokefn, void* gmfn) {
	++init;
	pfn_connect = connfn;
	pfn_typecheck = checkfn;
	pfn_invoke = invokefn;
	pfn_getmessage = gmfn;
	return 1;
}

//-----------------------------------------------------------------------------
// Postgres plandl call handler
//
// Once installed as a language handler, gets called by Postgres for every function.
//
Datum plandl_call_handler(PG_FUNCTION_ARGS) {
	++init;
	Oid funcOid = fcinfo->flinfo->fn_oid;
	Datum retval = { 0 };
	HeapTuple procTup;
	Form_pg_proc procStruct;

	procTup = SearchSysCache1(PROCOID, ObjectIdGetDatum(funcOid));
	if (!HeapTupleIsValid(procTup))
		elog(ERROR, "cache lookup failed for function %u", funcOid);
	procStruct = (Form_pg_proc)GETSTRUCT(procTup);

	// if not yet initialised...
	if (!init_ok) {
		if (strcmp(procStruct->proname.data, INIT_NAME) == 0) {
			bool isnull;
			Datum prosrcdatum = SysCacheGetAttr(PROCOID, procTup, Anum_pg_proc_prosrc, &isnull);
			if (isnull) elog(ERROR, "null prosrc");
			char* path = TextDatumGetCString(prosrcdatum);
			char* options = strchr(path, OPTION_DELIM);
			if (options) *options++ = '\0';
			else options = "";
			if (isnoisy) elog(NOTICE, "=== %s...init='%d' path='%s' options='%s'", MYNAME, init, path, options);
			handler_init(path, options); // does not return on failure
		} else elog(ERROR, "initialisation incomplete: function '%s'", procStruct->proname.data);
	}
	if (isnoisy) elog(NOTICE, "=== Call func='%s' args='%d'", procStruct->proname.data, procStruct->pronargs);

	// type check: should always succeed (unless someone screwed up)
	int ckret = pfn_typecheck(myinst, procStruct->proname.data, procStruct->pronargs, procStruct->proargtypes.values, procStruct->prorettype);
	if (ckret == 0) elog(ERROR, "(Type check) %s", pfn_getmessage(myinst));

	// invoke function: this is the payload
	int ivret = pfn_invoke(myinst, procStruct->proname.data, procStruct->pronargs, fcinfo->arg, &retval);
	if (ivret == 0) elog(ERROR, "(Invoke) %s", pfn_getmessage(myinst));

	if (isnoisy) elog(NOTICE, "=== Exit ret='%d' retval='%x'", ivret, retval);
	ReleaseSysCache(procTup);
	PG_RETURN_TEXT_P(retval);
}

//=============================================================================
// Exports, called by managed code

//-----------------------------------------------------------------------------
// Memory allocation based on palloc
//
void* pg_alloc_mem(int len) {
	return palloc(len);
}

void* pg_realloc_mem(void* ptr, int len) {
	return repalloc(ptr, len);
}

void* pg_alloc_copy(void* ptr, int len) {
	void* p = palloc(len);
	memcpy(p, ptr, len);
	return p;
}

// Allocate a datum of capacity len
// Note: storage is at offset p+4
void* pg_alloc_datum(int len) {
	Datum* p = palloc(VARHDRSZ + len);
	SET_VARSIZE(p, VARHDRSZ + len);
	return p;
}

char* hexdump(void* vp, int len) {
	char* pbuf = palloc0(len * 3 + 1);
	unsigned char* p = vp;
	for (char* pb = pbuf; len-- > 0; pb += 3) {
		sprintf(pb, "%02x ", *p++);
	}
	return pbuf;
}

//-----------------------------------------------------------------------------
// conversion wrappers
//
Datum pg_cstring_to_numeric(char* value) {
	return DirectFunctionCall3(numeric_in, CStringGetDatum(value), 0, -1);
}

char* pg_numeric_to_cstring(Datum value) {
	return DatumGetCString(DirectFunctionCall1(numeric_out, value));
}

Datum pg_cstring_to_timestamp(char* value) {
	return DirectFunctionCall3(timestamp_in, CStringGetDatum(value), 0, -1);
}

char* pg_timestamp_to_cstring(Datum value) {
	return DatumGetCString(DirectFunctionCall1(timestamp_out, value));
}

Datum pg_detoast_bytea(Datum value) {
	return (Datum)PG_DETOAST_DATUM(value);
}

void pg_elog(int elevel, char* message) {
	elog(elevel, message);
}

//-----------------------------------------------------------------------------
// Wrappers for SPI routines (which are not directly accessible)
//
// Regularise interface so all functions return same error code
//

// Enable access to SPI
int pg_spi_connect(void) {
	return SPI_connect();
}

// Shut down access to SPI
int	pg_spi_finish(void) {
	return SPI_finish();
}

// Execute a command, optionally will return one item of data
int	pg_spi_execute(const char *sql, bool read_only) {
	return SPI_execute(sql, read_only, 0);
}
// Parse a query and create a prepared statement for later execution
int pg_spi_prepare_cursor(const char *sql, int nargs, Oid *argtypes, int options, SPIPlanPtr* plan) {
	SPIPlanPtr p = SPI_prepare_cursor(sql, nargs, argtypes, options);
	if (p == NULL) return SPI_result;
	*plan = p;
	return SPI_OK;
}

// Execute a prepared statement with arguments
int pg_spi_execute_plan(SPIPlanPtr plan, int nvalues, Datum *values, bool read_only) {
	return SPI_execute_plan(plan, values, NULL, read_only, 0);
}

// Retrieve a single datum value (row 0.. col 1..)
int pg_spi_getdatum(int row, int column, Datum* datum) {
	bool isnull = false;
	if (row < 0 || row > (int)SPI_processed) return SPI_ERROR_ARGUMENT;
	Datum d = SPI_getbinval(SPI_tuptable->vals[row], SPI_tuptable->tupdesc, column, &isnull);
	if (SPI_result < 0) return SPI_result;
	if (isnull) elog(ERROR, "getdatum r=%d c=%d null", row, column);
	*datum = d;
	return SPI_OK;
}

// Execute a query, returning a cursor suitable for fetching
int pg_spi_cursor_execute(const char* sql, bool read_only, Portal* portal) {
	Portal p = SPI_cursor_open_with_args(NULL, sql, 0, NULL, NULL, NULL, read_only, 0);
	//elog(NOTICE, "=== cursex res=%d pro=%d", SPI_result, SPI_processed);
	if (p == NULL) return SPI_result; // not used
	*portal = p;
	return SPI_OK;
}

// Fetch next row, return SPI_OK_FETCH on success
int pg_spi_cursor_fetch(Portal portal) {
	SPI_cursor_fetch(portal, true, 1);
	//elog(NOTICE, "=== fetch res=%d pro=%d", SPI_result, SPI_processed);
	return (SPI_processed == 1) ?  SPI_OK_FETCH : SPI_OK;
}

