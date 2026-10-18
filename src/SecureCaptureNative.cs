using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
static class SecureCaptureNative {
 [StructLayout(LayoutKind.Sequential)]internal struct SERVICE_STATUS{internal uint ServiceType,CurrentState,ControlsAccepted,Win32ExitCode,ServiceSpecificExitCode,CheckPoint,WaitHint;}
 [StructLayout(LayoutKind.Sequential)]internal struct SERVICE_STATUS_PROCESS{internal uint ServiceType,CurrentState,ControlsAccepted,Win32ExitCode,ServiceSpecificExitCode,CheckPoint,WaitHint,ProcessId,ServiceFlags;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]internal struct STARTUPINFO{internal int cb;internal string lpReserved,lpDesktop,lpTitle;internal int dwX,dwY,dwXSize,dwYSize,dwXCountChars,dwYCountChars,dwFillAttribute,dwFlags;internal short wShowWindow,cbReserved2;internal IntPtr lpReserved2,hStdInput,hStdOutput,hStdError;}
 [StructLayout(LayoutKind.Sequential)]internal struct PROCESS_INFORMATION{internal IntPtr Process,Thread;internal uint ProcessId,ThreadId;}
 [StructLayout(LayoutKind.Sequential)]internal struct JOB_BASIC{internal long PerProcessUserTimeLimit,PerJobUserTimeLimit;internal uint LimitFlags;internal UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize;internal uint ActiveProcessLimit;internal UIntPtr Affinity;internal uint PriorityClass,SchedulingClass;}
 [StructLayout(LayoutKind.Sequential)]internal struct IO_COUNTERS{internal ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount;}
 [StructLayout(LayoutKind.Sequential)]internal struct JOB_LIMITS{internal JOB_BASIC Basic;internal IO_COUNTERS Io;internal UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed;}
 [StructLayout(LayoutKind.Sequential)]struct LUID{internal uint Low;internal int High;}
 [StructLayout(LayoutKind.Sequential)]struct TOKEN_PRIVILEGES{internal uint Count;internal LUID Luid;internal uint Attributes;}
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern IntPtr OpenSCManager(string machine,string database,uint access);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern IntPtr CreateService(IntPtr scm,string name,string display,uint access,uint type,uint start,uint error,string binary,string group,IntPtr tag,string dependencies,string account,string password);
 [DllImport("advapi32.dll",SetLastError=true)]internal static extern bool StartService(IntPtr service,int count,IntPtr args);
 [DllImport("advapi32.dll",SetLastError=true)]internal static extern bool ControlService(IntPtr service,uint control,out SERVICE_STATUS status);
 [DllImport("advapi32.dll",SetLastError=true)]internal static extern bool DeleteService(IntPtr service);
 [DllImport("advapi32.dll",SetLastError=true)]internal static extern bool CloseServiceHandle(IntPtr service);
 [DllImport("advapi32.dll",SetLastError=true)]internal static extern bool QueryServiceStatusEx(IntPtr service,int kind,out SERVICE_STATUS_PROCESS status,int size,out int needed);
 [DllImport("kernel32.dll")]internal static extern uint WTSGetActiveConsoleSessionId();
 [DllImport("wtsapi32.dll",SetLastError=true)]internal static extern bool WTSQueryUserToken(uint session,out IntPtr token);
 [DllImport("kernel32.dll")]internal static extern IntPtr GetCurrentProcess();
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool GetProcessTimes(IntPtr process,out long created,out long exited,out long kernel,out long user);
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool CloseHandle(IntPtr handle);
 [DllImport("advapi32.dll",SetLastError=true)]internal static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
 [DllImport("advapi32.dll",SetLastError=true)]internal static extern bool DuplicateTokenEx(IntPtr existing,uint access,IntPtr attributes,int impersonation,int kind,out IntPtr token);
 [DllImport("advapi32.dll",SetLastError=true)]internal static extern bool SetTokenInformation(IntPtr token,int kind,ref int value,int size);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool GetTokenInformation(IntPtr token,int kind,IntPtr info,int size,out int needed);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool LookupPrivilegeValue(string system,string name,out LUID value);
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool AdjustTokenPrivileges(IntPtr token,bool disable,ref TOKEN_PRIVILEGES privileges,int length,IntPtr previous,IntPtr needed);
 [DllImport("userenv.dll",SetLastError=true)]internal static extern bool CreateEnvironmentBlock(out IntPtr environment,IntPtr token,bool inherit);
 [DllImport("userenv.dll")]internal static extern bool DestroyEnvironmentBlock(IntPtr environment);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern IntPtr CreateJobObject(IntPtr attributes,string name);
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool SetInformationJobObject(IntPtr job,int kind,ref JOB_LIMITS limits,int size);
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool TerminateJobObject(IntPtr job,uint exit);
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern bool CreateProcessAsUser(IntPtr token,string app,StringBuilder command,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,IntPtr environment,string directory,ref STARTUPINFO startup,out PROCESS_INFORMATION process);
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern uint ResumeThread(IntPtr thread);
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool ProcessIdToSessionId(uint pid,out uint session);
 [DllImport("kernel32.dll")]internal static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool GetExitCodeProcess(IntPtr process,out uint exit);
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool TerminateProcess(IntPtr process,uint exit);
 internal static void Enable(string name) {IntPtr token;SecureCaptureRuntime.Native(OpenProcessToken(GetCurrentProcess(),0x28,out token),"Open privilege token");try{LUID luid;SecureCaptureRuntime.Native(LookupPrivilegeValue(null,name,out luid),"Find "+name);var privileges=new TOKEN_PRIVILEGES{Count=1,Luid=luid,Attributes=2};SecureCaptureRuntime.Native(AdjustTokenPrivileges(token,false,ref privileges,0,IntPtr.Zero,IntPtr.Zero),"Enable "+name);SecureCapturePolicy.Require(Marshal.GetLastWin32Error()!=1300,name+" is not available.");}finally{CloseHandle(token);}}
 internal static string TokenSid(IntPtr token) {int needed;GetTokenInformation(token,1,IntPtr.Zero,0,out needed);IntPtr buffer=Marshal.AllocHGlobal(needed);try{SecureCaptureRuntime.Native(GetTokenInformation(token,1,buffer,needed,out needed),"Read child SID");return new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;}finally{Marshal.FreeHGlobal(buffer);}}
}
