using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

sealed class SecureCaptureRuntime
{
    readonly SecureCapturePlan plan; readonly object logGate=new object();
    string logPath; bool runCreated; string previous;EventWaitHandle supervisorStop;
    internal SecureCaptureRuntime(SecureCapturePlan plan) {this.plan=plan;logPath=Path.Combine(plan.Root,"capture.log");}
    internal static void Native(bool ok,string message){if(!ok)throw new Win32Exception(Marshal.GetLastWin32Error(),message);}
    static bool IsSystem {get{using(var id=WindowsIdentity.GetCurrent())return id.User.IsWellKnown(WellKnownSidType.LocalSystemSid);}}
    static bool IsAdmin {get{using(var id=WindowsIdentity.GetCurrent())return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);}}
    internal void Log(string text){lock(logGate)File.AppendAllText(logPath,DateTime.UtcNow.ToString("o")+" "+text+Environment.NewLine);}
    internal static int Execute(string[] args)
    {
        try {
            if(args.Length==3&&args[0]=="-capture-request")return CaptureApproval.Relay(args);
            if(args.Length==3&&args[0]=="-capture-setup") {
                SecureCapturePolicy.Require(IsAdmin&&!IsSystem,"Human Windows administrator approval is required.");
                var p=SecureCapturePlan.Read(args[1],args[2]);
                SecureCapturePolicy.Require(SecureCapturePolicy.Same(SecureCaptureLauncher.Self,p.InstallExe)&&SecureCapturePolicy.Hash(SecureCaptureLauncher.Self)==p.SelfHash,"Capture setup binary changed.");
                SecureCapturePolicy.Require(HostNative.CurrentSession()==SecureCapturePolicy.SupervisorSession(p.ParentSession,p.Child)&&SecureCaptureLauncher.Sid==p.Sid,"Capture supervisor must belong to the bound parent user/session.");
                SecureCapturePolicy.Require(SecureCapturePolicy.Same(p.RequestPath,args[1]),"Capture request path changed.");
                var source=HostDiscovery.FromExecutable(p.SourceExecutable,new NativeHostDiscovery());
                SecureCapturePolicy.Require(source.Kind==HostKind.Sunshine&&new ChildHostProfile(source,p.ConfigurationSource,Path.GetDirectoryName(p.ConfigurationSource),p.BasePort).Identity==p.ProfileIdentity,"The selected Sunshine identity changed across Windows approval.");
                using(var current=Process.GetCurrentProcess()){p.SetupPid=current.Id;p.SetupTicks=current.StartTime.ToUniversalTime().Ticks;}
                return new SecureCaptureRuntime(p).Setup();
            }
            if(args.Length==2&&args[0]=="-capture-worker") {
                SecureCapturePolicy.Require(IsSystem&&HostNative.CurrentSession()==0,"Only the demand SYSTEM service may run capture.");
                string path=Path.Combine(Path.GetFullPath(args[1]),"plan.json");SecureCapturePolicy.Trusted(path,false);
                var p=SecureCapturePlan.Read(path,SecureCapturePolicy.Hash(path));
                SecureCapturePolicy.Require(SecureCapturePolicy.Same(p.Root,args[1])&&SecureCapturePolicy.Same(SecureCaptureLauncher.Self,Path.Combine(p.Root,"ChildStream.exe")),"Capture worker provenance changed.");
                SecureCapturePolicy.Trusted(p.ProfileRoot,true);SecureCapturePolicy.Trusted(p.Root,true);
                SecureCapturePolicy.Require(SecureCapturePolicy.Hash(SecureCaptureLauncher.Self)==p.SelfHash,"Capture worker binary changed.");
                ServiceBase.Run(new SecureCaptureService(new SecureCaptureRuntime(p),p.ServiceName));return 0;
            }
            return 2;
        } catch(Exception) {return 1;} // Never log plan/credential content or write an untrusted path as administrator.
    }
    void CheckProcess(int pid,long ticks,int session)
    {
        using(var process=Process.GetProcessById(pid)) {
            SecureCapturePolicy.Require(process.SessionId==session&&process.StartTime.ToUniversalTime().Ticks==ticks,"The bound viewer/helper/setup changed.");
            SecureCapturePolicy.Require(SecureCapturePolicy.Same(process.MainModule.FileName,plan.InstallExe),"The bound installation changed.");
        }
    }
    internal void VerifyBinding(){Binding();}
    void Binding()
    {
        SecureCapturePolicy.Require(HostNative.WTSGetActiveConsoleSessionId()==(uint)plan.ParentSession&&(uint)plan.Child!=HostNative.WTSGetActiveConsoleSessionId(),"The physical or child session changed.");
        CheckProcess(plan.ViewerPid,plan.ViewerTicks,plan.ParentSession);CheckProcess(plan.HelperPid,plan.HelperTicks,plan.Child);
        if(plan.SetupPid>0)CheckProcess(plan.SetupPid,plan.SetupTicks,SecureCapturePolicy.SupervisorSession(plan.ParentSession,plan.Child));
        if(!IsSystem)SecureCapturePolicy.Require(SecureCapturePolicy.Hash(plan.ConfigurationSource)==plan.ConfigurationHash,"The approved child configuration changed.");
    }
    void StartupBinding() {
        Binding();bool canceled=supervisorStop!=null&&supervisorStop.WaitOne(0);
        if(!IsSystem&&Directory.Exists(Path.GetDirectoryName(plan.RequestPath))) {
            SecureCapturePolicy.NoLinks(Path.GetDirectoryName(plan.RequestPath));
            canceled=canceled||File.Exists(plan.RequestPath+".cancel");
        }
        SecureCapturePolicy.EnsureStartupAllowed(plan.StartupDeadline,SecureCapturePolicy.Timestamp,canceled);
    }
    void PortsFree(){SecureCapturePolicy.Require(!HostPorts.Collides(plan.BasePort,HostPorts.Occupied()),"A child-host port is occupied. No existing host or listener was stopped.");}
    void Snapshot()
    {
        SecureCapturePolicy.Require(!Directory.Exists(plan.Root),"Capture run already exists.");
        Directory.CreateDirectory(plan.Root,SecureCapturePolicy.Acl(plan.Sid,false));runCreated=true;
        Directory.CreateDirectory(Path.Combine(plan.Root,"host"));
        foreach(var file in plan.Payload) {
            StartupBinding();
            string source=plan.SourceFile(file),target=SecureCapturePolicy.PayloadPath(Path.Combine(plan.Root,"host"),file.Relative);
            SecureCapturePolicy.NoLinks(source);SecureCapturePolicy.Require(new FileInfo(source).Length==file.Size&&SecureCapturePolicy.Hash(source)==file.Hash,"Selected host payload changed before approval.");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){input.CopyTo(output);output.Flush(true);}
            SecureCapturePolicy.Require(SecureCapturePolicy.Hash(target)==file.Hash,"Capture snapshot hash mismatch.");SecureCapturePolicy.Own(target,false);
        }
        File.Copy(plan.InstallExe,Path.Combine(plan.Root,"ChildStream.exe"),false);
        SecureCapturePolicy.Require(SecureCapturePolicy.Hash(Path.Combine(plan.Root,"ChildStream.exe"))==plan.SelfHash,"Capture executable copy changed.");
        SecureCapturePolicy.AtomicText(Path.Combine(plan.Root,"plan.json"),SecureCapturePlan.Serializer.Serialize(plan));
        foreach(string directory in Directory.GetDirectories(plan.Root,"*",SearchOption.AllDirectories))SecureCapturePolicy.Own(directory,true);
        foreach(string file in Directory.GetFiles(plan.Root,"*",SearchOption.AllDirectories))SecureCapturePolicy.Own(file,false);
    }
    void PrepareIdentity()
    {
        previous=SecureCaptureStore.SavedDirectory(plan.ProfileRoot);
        if(previous==null) {
            SecureCapturePolicy.Require(!File.Exists(Path.Combine(plan.ProfileRoot,"initialized"))&&plan.Seed.Length==4,"Protected pairing state is missing; stale original data will not be reimported.");
            string seed=Path.Combine(plan.Root,"import");
            SecureCaptureStore.CopyIdentity(plan.SourceData,seed);
            foreach(var file in plan.Seed)SecureCapturePolicy.Require(SecureCapturePolicy.Hash(Path.Combine(seed,file.Relative))==file.Hash,"Child pairing state changed across approval.");
            previous=SecureCaptureStore.Commit(plan.ProfileRoot,seed,null);
            SecureCapturePolicy.AtomicText(Path.Combine(plan.ProfileRoot,"initialized"),"Version 1: saved-generation is authoritative. Never reimport original child data.");
        }
        SecureCaptureStore.CopyIdentity(previous,plan.Data);
        SecureCapturePolicy.AtomicText(Path.Combine(plan.Data,"sunshine.conf"),SecureCapturePolicy.Configuration(plan.Data,Environment.MachineName,plan.BasePort));
        SecureCapturePolicy.AtomicText(Path.Combine(plan.Data,"apps.json"),"{\"env\":{},\"apps\":[{\"name\":\"Desktop\",\"image-path\":\"desktop.png\"}]}");
    }
    int Setup()
    {
        var acl=new EventWaitHandleSecurity();acl.SetAccessRuleProtection(true,false);acl.SetOwner(SecureCapturePolicy.Admin);
        foreach(var sid in new[]{SecureCapturePolicy.Admin,SecureCapturePolicy.SystemSid})acl.AddAccessRule(new EventWaitHandleAccessRule(sid,EventWaitHandleRights.FullControl,AccessControlType.Allow));
        acl.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(plan.Sid),EventWaitHandleRights.Modify|EventWaitHandleRights.Synchronize,AccessControlType.Allow));
        bool created;
        using(supervisorStop=new EventWaitHandle(false,EventResetMode.ManualReset,plan.EventName,out created,acl)) {
            SecureCapturePolicy.Require(created,"Capture stop event already exists.");
            StartupBinding();return SetupCore();
        }
    }
    int SetupCore()
    {
        IntPtr scm=IntPtr.Zero,service=IntPtr.Zero;bool stopped=false;int result=1;
        SecureCapturePolicy.NoLinks(Path.GetDirectoryName(SecureCapturePolicy.ParentRoot));
        SecureCapturePolicy.EnsureDirectory(SecureCapturePolicy.ParentRoot,plan.Sid,true);
        SecureCapturePolicy.EnsureDirectory(plan.ProfileRoot,plan.Sid,false);
        using(var lease=SecureCaptureStore.AcquireLease(plan.ProfileRoot))
        {
            try {
                StartupBinding();PortsFree();
                SecureCapturePolicy.EnsureDirectory(Path.Combine(plan.ProfileRoot,"runs"),plan.Sid,false);
                Snapshot();PrepareIdentity();StartupBinding();PortsFree();
                Log("Verified snapshot; child="+plan.Child+" base port="+plan.BasePort+". Demand service; no automatic startup.");
                scm=SecureCaptureNative.OpenSCManager(null,null,3);Native(scm!=IntPtr.Zero,"OpenSCManager");
                service=SecureCaptureNative.CreateService(scm,plan.ServiceName,"ChildStream child capture",0xf01ff,16,3,1,NativeArguments.Quote(Path.Combine(plan.Root,"ChildStream.exe"))+" -capture-worker "+NativeArguments.Quote(plan.Root),null,IntPtr.Zero,null,null,null);
                Native(service!=IntPtr.Zero,"Create child capture service");
                Native(SecureCaptureNative.StartService(service,0,IntPtr.Zero),"Start child capture service");
                Native(SecureCaptureNative.DeleteService(service),"Mark child capture service for deletion");
                var startup=Stopwatch.StartNew();
                for(;;) {
                    SecureCaptureNative.SERVICE_STATUS_PROCESS status;int needed;
                    Native(SecureCaptureNative.QueryServiceStatusEx(service,0,out status,Marshal.SizeOf(typeof(SecureCaptureNative.SERVICE_STATUS_PROCESS)),out needed),"Query capture service");
                    if(status.CurrentState==1){stopped=true;result=status.Win32ExitCode==0?0:1;break;}
                    var e=SecureCaptureLauncher.ReadEvidence(plan.ProfileRoot);
                    if((e==null||e.RunId!=plan.RunId)&&startup.ElapsedMilliseconds>60000)throw new System.TimeoutException("Capture service startup watchdog expired.");
                    if(e!=null&&e.RunId==plan.RunId&&e.Phase=="Running"&&!SecureCapturePolicy.FreshStatus(e.Heartbeat,SecureCapturePolicy.Timestamp)&&startup.ElapsedMilliseconds>15000)
                        throw new System.TimeoutException("Capture heartbeat watchdog expired.");
                    if(e!=null&&e.RunId==plan.RunId&&e.Phase=="Starting"&&startup.ElapsedMilliseconds>60000)throw new System.TimeoutException("Capture did not finish initializing.");
                    Binding();Thread.Sleep(500);
                }
            } catch(Exception ex) {if(runCreated)try{Log("FAILED "+ex.GetType().Name+": "+ex.Message);}catch(IOException){}}
            finally {
                if(service!=IntPtr.Zero) {
                    stopped=StopService(service);
                    SecureCaptureNative.DeleteService(service);SecureCaptureNative.CloseServiceHandle(service);
                } else stopped=true;
                if(scm!=IntPtr.Zero)SecureCaptureNative.CloseServiceHandle(scm);
                if(runCreated) {
                    bool firewallClean=true;
                    try{Firewall(false);}catch(Exception ex){firewallClean=false;try{Log("Firewall cleanup failed: "+ex.Message);}catch(IOException){}}
                    bool confirmed=stopped&&firewallClean&&(service==IntPtr.Zero||File.Exists(Path.Combine(plan.Root,"shutdown.confirmed")));
                    try {
                        string summary=File.Exists(logPath)?SecureCapturePolicy.ReadBounded(logPath,1024*1024):"Capture did not complete snapshot preparation.";
                        SecureCapturePolicy.AtomicText(Path.Combine(plan.ProfileRoot,"last-run.log"),summary);
                    }catch(IOException){}
                    if(confirmed) {
                        bool removed=false;
                        for(int attempt=0;attempt<20&&!removed;attempt++)try{RemoveRun();removed=true;}catch(IOException){Thread.Sleep(250);}catch(UnauthorizedAccessException){Thread.Sleep(250);}
                        if(!removed)result=1;
                    } else {
                        result=1; // Keep failed working identity for recovery; never delete an unconfirmed live payload.
                    }
                }
            }
        }
        return result;
    }
    bool StopService(IntPtr service)
    {
        try {
            SecureCaptureNative.SERVICE_STATUS_PROCESS status;int needed;
            if(!SecureCaptureNative.QueryServiceStatusEx(service,0,out status,Marshal.SizeOf(typeof(SecureCaptureNative.SERVICE_STATUS_PROCESS)),out needed))return false;
            if(status.CurrentState==1)return true;
            SecureCaptureNative.SERVICE_STATUS basic;SecureCaptureNative.ControlService(service,1,out basic);
            var timer=Stopwatch.StartNew();
            while(timer.ElapsedMilliseconds<15000) {
                if(SecureCaptureNative.QueryServiceStatusEx(service,0,out status,Marshal.SizeOf(typeof(SecureCaptureNative.SERVICE_STATUS_PROCESS)),out needed)&&status.CurrentState==1)return true;
                Thread.Sleep(200);
            }
            // SCM stop is bounded. Leave evidence/payload rather than target an unverified SYSTEM PID.
            return false;
        } catch(Win32Exception){return false;}
    }
    void RemoveRun()
    {
        string expected=SecureCapturePolicy.RunRoot(plan.ProfileRoot,plan.RunId);
        SecureCapturePolicy.Require(SecureCapturePolicy.Same(expected,plan.Root),"Capture cleanup root mismatch.");
        SecureCapturePolicy.NoLinks(plan.Root);
        foreach(string entry in Directory.GetFileSystemEntries(plan.Root,"*",SearchOption.AllDirectories))
            SecureCapturePolicy.Require((File.GetAttributes(entry)&FileAttributes.ReparsePoint)==0,"Capture cleanup refused a reparse point.");
        Directory.Delete(plan.Root,true);
    }
    void Evidence(string phase,int hostPid,long hostTicks,string message)
    {
        var e=new SecureCaptureEvidence{RunId=plan.RunId,SourceExecutable=plan.SourceExecutable,ActualExecutable=plan.HostExe,ProfileIdentity=plan.ProfileIdentity,
            Phase=phase,Message=message,Child=plan.Child,HostPid=hostPid,HostTicks=hostTicks,WorkerPid=Process.GetCurrentProcess().Id,
            SetupPid=plan.SetupPid,ParentSession=plan.ParentSession,ViewerPid=plan.ViewerPid,HelperPid=plan.HelperPid,Heartbeat=SecureCapturePolicy.Timestamp,BasePort=plan.BasePort};
        SecureCapturePolicy.AtomicText(Path.Combine(plan.ProfileRoot,"current-run.json"),new JavaScriptSerializer().Serialize(e));
    }
    internal bool Worker(WaitHandle serviceStop)
    {
        bool failed=false,assigned=false,hostStopped=false,firewallClean=false;
        IntPtr current=IntPtr.Zero,token=IntPtr.Zero,job=IntPtr.Zero,environment=IntPtr.Zero;
        SecureCaptureNative.PROCESS_INFORMATION process=new SecureCaptureNative.PROCESS_INFORMATION();EventWaitHandle publicStop=null;
        int hostPid=0;long hostTicks=0;
        try {
            Binding();PortsFree();
            SecureCaptureNative.Enable("SeTcbPrivilege");SecureCaptureNative.Enable("SeAssignPrimaryTokenPrivilege");SecureCaptureNative.Enable("SeIncreaseQuotaPrivilege");
            IntPtr user;Native(SecureCaptureNative.WTSQueryUserToken((uint)plan.Child,out user),"Query child user token");
            try{SecureCapturePolicy.Require(SecureCaptureNative.TokenSid(user)==plan.Sid,"Child user changed.");}finally{SecureCaptureNative.CloseHandle(user);}
            publicStop=EventWaitHandle.OpenExisting(plan.EventName,EventWaitHandleRights.FullControl);
            var owner=publicStop.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
            SecureCapturePolicy.Require(owner.Equals(SecureCapturePolicy.Admin)||owner.Equals(SecureCapturePolicy.SystemSid),"Capture stop event has an untrusted owner.");
            SecureCapturePolicy.EnsureStartupAllowed(plan.StartupDeadline,SecureCapturePolicy.Timestamp,publicStop.WaitOne(0));
            Evidence("Starting",0,0,"Validating protected host snapshot.");
            foreach(var file in plan.Payload){string path=SecureCapturePolicy.PayloadPath(Path.Combine(plan.Root,"host"),file.Relative);SecureCapturePolicy.Trusted(path,false);SecureCapturePolicy.Require(SecureCapturePolicy.Hash(path)==file.Hash,"Protected capture payload changed.");}
            previous=SecureCaptureStore.SavedDirectory(plan.ProfileRoot);
            Native(SecureCaptureNative.OpenProcessToken(SecureCaptureNative.GetCurrentProcess(),10,out current),"Open SYSTEM token");
            Native(SecureCaptureNative.DuplicateTokenEx(current,0xf01ff,IntPtr.Zero,2,1,out token),"Duplicate SYSTEM token");
            int child=plan.Child;Native(SecureCaptureNative.SetTokenInformation(token,12,ref child,4),"Bind capture token to child");
            Native(SecureCaptureNative.CreateEnvironmentBlock(out environment,token,false),"Create capture environment");
            job=SecureCaptureNative.CreateJobObject(IntPtr.Zero,null);Native(job!=IntPtr.Zero,"Create capture lifetime job");
            var limits=new SecureCaptureNative.JOB_LIMITS();limits.Basic.LimitFlags=0x2000;
            Native(SecureCaptureNative.SetInformationJobObject(job,9,ref limits,Marshal.SizeOf(typeof(SecureCaptureNative.JOB_LIMITS))),"Set capture fail-closed lifetime");
            Firewall(true);Binding();PortsFree();
            var startup=new SecureCaptureNative.STARTUPINFO();startup.cb=Marshal.SizeOf(typeof(SecureCaptureNative.STARTUPINFO));startup.lpDesktop=@"winsta0\default";
            Native(SecureCaptureNative.CreateProcessAsUser(token,plan.HostExe,new StringBuilder(NativeArguments.Join(new[]{plan.HostExe,Path.Combine(plan.Data,"sunshine.conf")})),IntPtr.Zero,IntPtr.Zero,false,0x08000404,environment,Path.GetDirectoryName(plan.HostExe),ref startup,out process),"Start SYSTEM capture in child");
            Native(SecureCaptureNative.AssignProcessToJobObject(job,process.Process),"Attach capture to lifetime job");assigned=true;
            uint actual;Native(SecureCaptureNative.ProcessIdToSessionId(process.ProcessId,out actual),"Query capture session");
            SecureCapturePolicy.Require(actual==(uint)plan.Child,"Capture process did not enter the child session.");
            long createdAt,exited,kernel,used;Native(SecureCaptureNative.GetProcessTimes(process.Process,out createdAt,out exited,out kernel,out used),"Query capture creation time");
            hostPid=checked((int)process.ProcessId);hostTicks=DateTime.FromFileTimeUtc(createdAt).Ticks;
            Binding();SecureCapturePolicy.EnsureStartupAllowed(plan.StartupDeadline,SecureCapturePolicy.Timestamp,publicStop.WaitOne(0));
            Native(SecureCaptureNative.ResumeThread(process.Thread)!=0xffffffff,"Resume child capture");
            var ready=Stopwatch.StartNew();bool listening=false;
            while(ready.ElapsedMilliseconds<45000) {
                if(serviceStop.WaitOne(0)||publicStop.WaitOne(0))throw new OperationCanceledException("Capture startup was canceled.");
                if(SecureCaptureNative.WaitForSingleObject(process.Process,0)==0)throw new IOException("Sunshine exited during protected startup. Check its protected sunshine.log.");
                Binding();SecureCapturePolicy.EnsureStartupAllowed(plan.StartupDeadline,SecureCapturePolicy.Timestamp,publicStop.WaitOne(0));var ports=HostPorts.Occupied();listening=ports.Any(p=>p.Tcp&&p.Port==plan.BasePort)&&ports.Any(p=>p.Tcp&&p.Port==plan.BasePort+1);
                Evidence(listening?"Running":"Starting",hostPid,hostTicks,"LOCAL_SYSTEM child capture; ddx; Desktop workflow.");if(listening)break;
                Thread.Sleep(500);
            }
            SecureCapturePolicy.Require(listening,"Sunshine did not initialize its protocol and web listeners.");
            Log("RUNNING host="+hostPid+" child="+plan.Child+" account=LOCAL_SYSTEM capture=ddx; no console fallback.");
            while(!serviceStop.WaitOne(500)&&!publicStop.WaitOne(0)) {
                if(SecureCaptureNative.WaitForSingleObject(process.Process,0)==0)throw new IOException("The protected host exited.");
                Binding();Evidence("Running",hostPid,hostTicks,"Protected child capture.");
            }
        } catch(Exception ex){failed=true;try{Log("FAILED "+ex.GetType().Name+": "+ex.Message);}catch(IOException){}}
        finally {
            bool jobStopped=job==IntPtr.Zero||SecureCaptureNative.TerminateJobObject(job,0);
            if(process.Process!=IntPtr.Zero) {
                if(!assigned&&SecureCaptureNative.WaitForSingleObject(process.Process,0)!=0)SecureCaptureNative.TerminateProcess(process.Process,0);
                hostStopped=SecureCaptureNative.WaitForSingleObject(process.Process,5000)==0;
            } else hostStopped=true;
            if(job!=IntPtr.Zero)SecureCaptureNative.CloseHandle(job);
            if(process.Thread!=IntPtr.Zero)SecureCaptureNative.CloseHandle(process.Thread);if(process.Process!=IntPtr.Zero)SecureCaptureNative.CloseHandle(process.Process);
            if(environment!=IntPtr.Zero)SecureCaptureNative.DestroyEnvironmentBlock(environment);
            if(token!=IntPtr.Zero)SecureCaptureNative.CloseHandle(token);if(current!=IntPtr.Zero)SecureCaptureNative.CloseHandle(current);
            try{Firewall(false);firewallClean=true;}catch(Exception ex){failed=true;try{Log("Firewall cleanup: "+ex.Message);}catch(IOException){}}
            if(publicStop!=null)publicStop.Dispose();
            bool saved=false;
            if(jobStopped&&hostStopped&&firewallClean) {
                try{SecureCaptureStore.Commit(plan.ProfileRoot,plan.Data,previous);saved=true;}catch(Exception ex){failed=true;try{Log("Pairing update was not committed; last-good identity retained. "+ex.GetType().Name);}catch(IOException){}}
                try{Evidence(saved?"Stopped":"RecoveryRequired",hostPid,hostTicks,saved?"Host/job stopped; pairing committed.":"Host/job stopped; working identity retained for recovery.");}catch(IOException){failed=true;}
                if(saved)SecureCapturePolicy.AtomicText(Path.Combine(plan.Root,"shutdown.confirmed"),"Private host/job stopped; exact firewall rules removed; valid pairing generation committed.");
            } else {failed=true;try{Log("Host/job/firewall teardown was not confirmed; run payload retained.");}catch(IOException){}}
            Log("Capture worker finished. Existing hosts and apps were not stopped.");
        }
        return !failed;
    }
    internal void Firewall(bool add)
    {
        object policyObject=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));dynamic policy=policyObject;
        try{foreach(int protocol in new[]{6,17}) {
            string name=plan.ServiceName+"-"+protocol;
            if(!add) {
                bool exists=false;
                foreach(dynamic old in policy.Rules)if(string.Equals((string)old.Name,name,StringComparison.OrdinalIgnoreCase)){SecureCapturePolicy.Require(SecureCapturePolicy.Same((string)old.ApplicationName,plan.HostExe)&&(int)old.Protocol==protocol,"Capture firewall ownership mismatch.");exists=true;}
                if(exists)policy.Rules.Remove(name);
                foreach(dynamic old in policy.Rules)SecureCapturePolicy.Require(!string.Equals((string)old.Name,name,StringComparison.OrdinalIgnoreCase),"Capture firewall rule removal failed.");
                continue;
            }
            object ruleObject=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule"));dynamic rule=ruleObject;
            try {
                rule.Name=name;rule.Description="ChildStream child capture, local subnet only.";rule.ApplicationName=plan.HostExe;rule.Protocol=protocol;
                rule.LocalPorts=string.Join(",",HostPorts.Family(plan.BasePort).Where(p=>p.Tcp==(protocol==6)).Select(p=>p.Port));
                rule.RemoteAddresses="LocalSubnet";rule.Direction=1;rule.Action=1;rule.Profiles=7;rule.Enabled=true;policy.Rules.Add(rule);
            }finally{Marshal.ReleaseComObject(ruleObject);}
        }}finally{Marshal.ReleaseComObject(policyObject);}
    }
}
sealed class SecureCaptureService:ServiceBase
{
    readonly SecureCaptureRuntime runtime;readonly ManualResetEvent stop=new ManualResetEvent(false);Thread worker;
    internal SecureCaptureService(SecureCaptureRuntime runtime,string name){this.runtime=runtime;ServiceName=name;CanStop=true;AutoLog=false;}
    protected override void OnStart(string[] args) {
        worker=new Thread(delegate(){try{if(!runtime.Worker(stop))ExitCode=1;}finally{ThreadPool.QueueUserWorkItem(delegate{Stop();});}});
        worker.IsBackground=true;worker.Start();
    }
    protected override void OnStop(){stop.Set();if(worker!=null&&worker!=Thread.CurrentThread&&!worker.Join(10000)){ExitCode=1;}}
    protected override void Dispose(bool disposing){if(disposing)stop.Dispose();base.Dispose(disposing);}
}
