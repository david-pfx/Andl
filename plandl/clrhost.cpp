// Load CLR via COM hosting interface

// See http://msdn.microsoft.com/en-us/library/dd380851.aspx
// See sample for CppHostCLR

#define _CRT_SECURE_NO_WARNINGS

#pragma region Includes and Imports
#include <windows.h>

#include <metahost.h>
#pragma comment(lib, "mscoree.lib")

// Import mscorlib.tlb (Microsoft Common Language Runtime Class Library).
//#import "mscorlib.tlb" raw_interfaces_only				\
//    high_property_prefixes("_get","_put","_putref")		\
//    rename("ReportEvent", "InteropServices_ReportEvent")
//using namespace mscorlib;
#pragma endregion

// Constants defining what will be loaded
PCWSTR pszVersion = L"v4.0.30319";
PCWSTR pszClassName = L"Andl.Gateway.Postgres";
PCWSTR pszStaticMethodName = L"Entry";
PCWSTR pszStringArg = L"dummy argument";

// COM interfaces needed
ICLRMetaHost *pMetaHost = NULL;
ICLRRuntimeInfo *pRuntimeInfo = NULL;
ICLRRuntimeHost *pClrRuntimeHost = NULL;

// error info retrieved if return false
static int _lasterr = 0;
static char* _lastmsg = "";

// entry points
extern "C" {
	int load_clr(char *path);
	int lasterr() { return _lasterr; }
	char* lastmsg() { return _lastmsg; }
}

// set error and release interfaces
int set_err(int err, char* msg) {
	_lasterr = err;
	_lastmsg = msg;
	if (pMetaHost) pMetaHost->Release();
	if (pRuntimeInfo) pRuntimeInfo->Release();
	return 0;
}

// main entry point to host CLR
int load_clr(char *assembly_path) {
	WCHAR assembly[260];
	mbstowcs(assembly, assembly_path, 260);
	
	int hr;
	hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_PPV_ARGS(&pMetaHost));
	if (FAILED(hr)) return set_err(hr, "CreateInstance failed");

	hr = pMetaHost->GetRuntime(pszVersion, IID_PPV_ARGS(&pRuntimeInfo));
	if (FAILED(hr)) return set_err(hr, "GetRuntime failed");

	BOOL fLoadable;
	hr = pRuntimeInfo->IsLoadable(&fLoadable);
	if (FAILED(hr)) return set_err(hr, "IsLoadable failed");
	if (!fLoadable) return set_err(0, "not loadable");

	hr = pRuntimeInfo->GetInterface(CLSID_CLRRuntimeHost, IID_PPV_ARGS(&pClrRuntimeHost));
	if (FAILED(hr)) return set_err(hr, "get CLRRuntimeHost failed");

	hr = pClrRuntimeHost->Start();
	if (FAILED(hr)) return set_err(hr, "CLR failed to start");

	DWORD dwLengthRet;
	hr = pClrRuntimeHost->ExecuteInDefaultAppDomain(assembly,
		pszClassName, pszStaticMethodName, pszStringArg, &dwLengthRet);
	if (FAILED(hr)) return set_err(hr, "call method failed");

	_lasterr = dwLengthRet;
	_lastmsg = "OK";

	return 1;
}