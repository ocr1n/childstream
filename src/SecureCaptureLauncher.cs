using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

sealed class CapturePayloadFile { public string Relative {get;set;} public string Hash {get;set;} public long Size {get;set;} }
sealed class SecureCapturePlan
{
    public string RequestPath {get;set;} public long StartupDeadline {get;set;}
    public string RunId {get;set;} public string InstallExe {get;set;} public string SelfHash {get;set;}
    public string SourceExecutable {get;set;} public string Sid {get;set;} public string ProfileIdentity {get;set;}
    public string ConfigurationSource {get;set;} public string ConfigurationHash {get;set;}
    public int Child {get;set;} public int ParentSession {get;set;} public int BasePort {get;set;}
    public int ViewerPid {get;set;} public long ViewerTicks {get;set;} public int HelperPid {get;set;} public long HelperTicks {get;set;}
    public int SetupPid {get;set;} public long SetupTicks {get;set;}
    public CapturePayloadFile[] Payload {get;set;} public CapturePayloadFile[] Seed {get;set;}
    internal string InstallRoot {get{return Path.GetDirectoryName(InstallExe);}}
    internal string ProfileRoot {get{return SecureCapturePolicy.ProfileRoot(InstallRoot,Sid,SourceExecutable);}}
    internal string Root {get{return SecureCapturePolicy.RunRoot(ProfileRoot,RunId);}}
    internal string SourceRoot {get{return Path.GetDirectoryName(SourceExecutable);}}
    internal string SourceData {get{return Path.GetDirectoryName(ConfigurationSource);}}
    internal string Data {get{return Path.Combine(Root,"data");}}
    internal string HostExe {get{return Path.Combine(Root,"host","sunshine.exe");}}
    internal string ServiceName {get{return "ChildStreamCapture"+RunId;}}
    internal string EventName {get{return @"Global\ChildStreamCaptureStop"+RunId;}}
    internal string SourceFile(CapturePayloadFile file) {return file.Relative.Equals("sunshine.exe",StringComparison.OrdinalIgnoreCase)?SourceExecutable:SecureCapturePolicy.PayloadPath(SourceRoot,file.Relative);}
    internal static JavaScriptSerializer Serializer {get{return new JavaScriptSerializer{MaxJsonLength=2*1024*1024,RecursionLimit=16};}}
    internal static SecureCapturePlan Read(string path,string expectedHash) {
        SecureCapturePolicy.Require(expectedHash!=null&&expectedHash.Length==64&&expectedHash.All(Uri.IsHexDigit),"Invalid launch-plan digest.");
        SecureCapturePolicy.NoLinks(path);
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
            SecureCapturePolicy.Require(stream.Length>0&&stream.Length<=2*1024*1024,"Invalid capture launch-plan size.");
            byte[] bytes=new byte[(int)stream.Length];int done=0;while(done<bytes.Length){int n=stream.Read(bytes,done,bytes.Length-done);if(n==0)throw new EndOfStreamException();done+=n;}
            using(var sha=System.Security.Cryptography.SHA256.Create())SecureCapturePolicy.Require(SecureCapturePolicy.Hex(sha.ComputeHash(bytes)).Equals(expectedHash,StringComparison.OrdinalIgnoreCase),"The capture launch plan changed across Windows approval.");
            var plan=Serializer.Deserialize<SecureCapturePlan>(Encoding.UTF8.GetString(bytes));plan.Validate();return plan;
        }
    }
    internal void Validate() {
        Guid id;SecureCapturePolicy.Require(Guid.TryParseExact(RunId,"N",out id),"Invalid run identity.");
        SecureCapturePolicy.Require(!string.IsNullOrWhiteSpace(RequestPath)&&Path.IsPathRooted(RequestPath)&&!RequestPath.StartsWith(@"\")&&Path.GetFileName(RequestPath)=="plan.json"&&Path.GetFileName(Path.GetDirectoryName(RequestPath))=="ChildStream-capture-request-"+RunId,"Invalid normal-user request directory.");
        SecureCapturePolicy.Require(StartupDeadline<=SecureCapturePolicy.Timestamp+TimeSpan.FromMinutes(5).Ticks,"Capture approval expiry is too far in the future.");
        SecureCapturePolicy.EnsureStartupAllowed(StartupDeadline,SecureCapturePolicy.Timestamp,false);
        SecureCapturePolicy.Require(Child>0&&ParentSession>0&&Child!=ParentSession,"Invalid child binding.");HostPorts.Family(BasePort);
        new SecurityIdentifier(Sid);
        SecureCapturePolicy.Require(Path.IsPathRooted(InstallExe)&&Path.IsPathRooted(SourceExecutable)&&Path.IsPathRooted(ConfigurationSource),"Capture paths must be absolute.");
        SecureCapturePolicy.Require(ProfileIdentity!=null&&ProfileIdentity.Length==64&&ProfileIdentity.All(Uri.IsHexDigit)&&SelfHash!=null&&SelfHash.Length==64&&ConfigurationHash!=null&&ConfigurationHash.Length==64,"Invalid capture identity.");
        string normal=Path.Combine(InstallRoot,"Hosts",HostProfiles.Digest(HostKind.Sunshine+"|"+Path.GetFullPath(SourceExecutable).ToUpperInvariant()),"sunshine.conf");
        string legacy=Path.Combine(InstallRoot,"Sunshine","Sunshine","config","sunshine.conf");
        bool legacyHost=SecureCapturePolicy.Same(SourceExecutable,Path.Combine(InstallRoot,"Sunshine","Sunshine","sunshine.exe"));
        SecureCapturePolicy.Require(SecureCapturePolicy.Same(ConfigurationSource,legacyHost?legacy:normal),"Only this installation's separate child profile can be captured.");
        SecureCapturePolicy.Require(ViewerPid>0&&HelperPid>0&&ViewerPid!=HelperPid&&ViewerTicks>0&&HelperTicks>0,"Invalid process binding.");
        SecureCapturePolicy.Require(Payload!=null&&Payload.Length>0&&Payload.Length<=2048&&Seed!=null&&Seed.Length<=4,"Invalid snapshot manifest.");
        long size=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in Payload){SecureCapturePolicy.PayloadPath(Root,file.Relative);SecureCapturePolicy.Require(seen.Add(file.Relative)&&file.Hash!=null&&file.Hash.Length==64&&file.Hash.All(Uri.IsHexDigit)&&file.Size>=0&&file.Size<=512L*1024*1024,"Invalid snapshot file.");size+=file.Size;}
        SecureCapturePolicy.Require(size<=512L*1024*1024&&seen.Contains("sunshine.exe"),"The selected host snapshot exceeds 512 MiB or omits Sunshine.");
        seen.Clear();foreach(var file in Seed)SecureCapturePolicy.Require(file!=null&&SecureCapturePolicy.IdentityFiles.Contains(file.Relative)&&seen.Add(file.Relative)&&file.Hash!=null&&file.Hash.Length==64&&file.Hash.All(Uri.IsHexDigit)&&file.Size>0&&file.Size<=8*1024*1024,"Invalid child identity seed.");
    }
}
sealed class SecureCaptureEvidence
{
    public string RunId {get;set;} public string SourceExecutable {get;set;} public string ActualExecutable {get;set;}
    public string ProfileIdentity {get;set;} public string Phase {get;set;} public string Message {get;set;}
    public int Child {get;set;} public int ParentSession {get;set;} public int HostPid {get;set;} public long HostTicks {get;set;}
    public int WorkerPid {get;set;} public int SetupPid {get;set;} public int ViewerPid {get;set;} public int HelperPid {get;set;}
    public long Heartbeat {get;set;} public int BasePort {get;set;}
}
static class SecureCaptureLauncher
{
    internal static string Self {get{return Assembly.GetExecutingAssembly().Location;}}
    internal static string Sid {get{using(var id=WindowsIdentity.GetCurrent())return id.User.Value;}}
    static CapturePayloadFile FileRecord(string path,string relative) {
        SecureCapturePolicy.NoLinks(path);
        return new CapturePayloadFile{Relative=relative,Hash=SecureCapturePolicy.Hash(path),Size=new FileInfo(path).Length};
    }
    internal static bool HasSaved(string root) {
        if(!Directory.Exists(root))return false;
        string saved=SecureCaptureStore.SavedDirectory(root);
        if(saved==null&&File.Exists(Path.Combine(root,"initialized")))throw new IOException("The protected pairing pointer is missing. Restore its last-good generation; original child data was not reimported.");
        return saved!=null;
    }
    internal static bool CanSeed(string directory) {try{SecureCaptureStore.ValidateIdentity(directory);return true;}catch(IOException){return false;}catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}}
    internal static bool Available(ChildHostProfile profile) {
        if(profile.Installation.Kind!=HostKind.Sunshine)return false;
        string root=SecureCapturePolicy.ProfileRoot(Path.GetDirectoryName(Self),Sid,profile.Installation.ExecutablePath);
        return HasSaved(root)||CanSeed(profile.DataDirectory);
    }
    static void CollectAssets(string source,string current,List<CapturePayloadFile> files,int depth) {
        SecureCapturePolicy.Require(depth<=16&&files.Count<=2048,"Host assets exceed the capture snapshot limits.");SecureCapturePolicy.NoLinks(current);
        foreach(string file in Directory.GetFiles(current)) {
            string relative=file.Substring(source.TrimEnd('\\').Length+1);SecureCapturePolicy.PayloadPath(source,relative);
            files.Add(FileRecord(file,relative));SecureCapturePolicy.Require(files.Count<=2048,"Too many host assets.");
        }
        foreach(string child in Directory.GetDirectories(current))CollectAssets(source,child,files,depth+1);
    }
    internal static SecureCapturePlan CreatePlan(ChildHostProfile profile,int viewerPid) {
        using(var current=Process.GetCurrentProcess()) {
            int console=checked((int)HostNative.WTSGetActiveConsoleSessionId());
            SecureCapturePolicy.Require(current.SessionId>0&&current.SessionId!=console,"Capture must be requested from the actual child helper.");
            SecureCapturePolicy.Require(viewerPid>0,"An authenticated viewer identity is required.");
            using(var candidate=Process.GetProcessById(viewerPid))
                SecureCapturePolicy.Require(candidate.SessionId==console&&SecureCapturePolicy.Same(HostNative.ProcessPath((uint)viewerPid),Self),"The authenticated viewer left its installation or parent session.");
            var viewer=Process.GetProcessById(viewerPid);
            using(viewer) {
                var plan=new SecureCapturePlan{RunId=Guid.NewGuid().ToString("N"),InstallExe=Self,SelfHash=SecureCapturePolicy.Hash(Self),
                    SourceExecutable=profile.Installation.ExecutablePath,Sid=Sid,ProfileIdentity=profile.Identity,ConfigurationSource=profile.ConfigPath,
                    ConfigurationHash=SecureCapturePolicy.Hash(profile.ConfigPath),Child=current.SessionId,ParentSession=console,BasePort=profile.BasePort,
                    ViewerPid=viewer.Id,ViewerTicks=viewer.StartTime.ToUniversalTime().Ticks,HelperPid=current.Id,HelperTicks=current.StartTime.ToUniversalTime().Ticks};
                plan.RequestPath=Path.Combine(Path.GetTempPath(),"ChildStream-capture-request-"+plan.RunId,"plan.json");
                plan.StartupDeadline=SecureCapturePolicy.Timestamp+TimeSpan.FromMinutes(5).Ticks;
                var files=new List<CapturePayloadFile>{FileRecord(plan.SourceExecutable,"sunshine.exe")};
                foreach(string file in Directory.GetFiles(plan.SourceRoot,"*.dll"))files.Add(FileRecord(file,Path.GetFileName(file)));
                string assets=Path.Combine(plan.SourceRoot,"assets");if(Directory.Exists(assets))CollectAssets(plan.SourceRoot,assets,files,0);
                plan.Payload=files.ToArray();plan.Seed=HasSaved(plan.ProfileRoot)?new CapturePayloadFile[0]:SecureCapturePolicy.IdentityFiles.Select(name=>FileRecord(Path.Combine(profile.DataDirectory,name),name)).ToArray();
                plan.Validate();return plan;
            }
        }
    }
    internal static SecureCaptureEvidence ReadEvidence(string root) {
        string path=Path.Combine(root,"current-run.json");if(!File.Exists(path))return null;
        SecureCapturePolicy.Trusted(root,true);SecureCapturePolicy.Trusted(path,false);
        return new JavaScriptSerializer{MaxJsonLength=8192,RecursionLimit=8}.Deserialize<SecureCaptureEvidence>(SecureCapturePolicy.ReadBounded(path,8192));
    }
    internal static HostSnapshot Status(ChildHostProfile profile,int session) {
        if(profile.Installation.Kind!=HostKind.Sunshine)return null;
        string root=SecureCapturePolicy.ProfileRoot(Path.GetDirectoryName(Self),Sid,profile.Installation.ExecutablePath);
        if(!Directory.Exists(root))return null;
        try {
            var e=ReadEvidence(root);if(e==null)return null;
            return SecureCapturePolicy.EvidenceStatus(e,root,profile.Installation.ExecutablePath,profile.Identity,session,SecureCapturePolicy.Timestamp,
                pid=>{using(var process=Process.GetProcessById(pid))return process.SessionId;});
        } catch(Exception ex){return new HostSnapshot(HostState.Unknown,"Cannot verify protected child capture: "+ex.Message,0);}
    }
    internal static HostLaunchReceipt Start(ChildHostProfile profile,int viewerPid) {
        var plan=CreatePlan(profile,viewerPid);
        string path=plan.RequestPath,scratch=Path.GetDirectoryName(path);Directory.CreateDirectory(scratch);
        File.WriteAllText(path,SecureCapturePlan.Serializer.Serialize(plan),new UTF8Encoding(false));
        bool success=false;
        try {
            using(var pinned=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                CaptureApproval.Start(plan,path,SecureCapturePolicy.Hash(path));
                var approval=Stopwatch.StartNew();
                while(approval.ElapsedMilliseconds<180000) {
                    string reply=ReadApproval(path);
                    if(reply=="canceled")throw new Win32Exception(1223);
                    if(reply=="approved")break;
                    if(reply=="failed"||reply.StartsWith("exit="))throw new IOException("The protected capture approval relay exited before startup.");
                    Thread.Sleep(100);
                }
                if(approval.ElapsedMilliseconds>=180000)throw new TimeoutException("Windows approval did not complete. Nothing will start from this expired request.");
                var clock=Stopwatch.StartNew();
                while(clock.ElapsedMilliseconds<90000) {
                    var evidence=ReadEvidence(plan.ProfileRoot);
                    if(evidence!=null&&evidence.RunId==plan.RunId&&evidence.Phase=="Running"&&SecureCapturePolicy.FreshStatus(evidence.Heartbeat,SecureCapturePolicy.Timestamp)) {
                        success=true;return new HostLaunchReceipt(evidence.HostPid,evidence.HostTicks,profile.Identity);
                    }
                    string reply=ReadApproval(path);
                    if(reply=="failed"||reply=="canceled"||reply.StartsWith("exit="))throw new IOException("Protected capture did not start. Check "+Path.Combine(plan.ProfileRoot,"last-run.log")+".");
                    Thread.Sleep(100);
                }
                throw new TimeoutException("Protected capture exceeded its startup deadline; cancellation has been requested.");
            }
        } finally {
            if(!success) {
                try{File.WriteAllText(path+".cancel","Canceled: this request must never launch later.");}catch(IOException){}catch(UnauthorizedAccessException){}
                try{using(var stop=EventWaitHandle.OpenExisting(plan.EventName,System.Security.AccessControl.EventWaitHandleRights.Modify))stop.Set();}catch(WaitHandleCannotBeOpenedException){}catch(UnauthorizedAccessException){}
                // Keep this deny-only marker until the five-minute pinned plan expiry.
                // Elevated setup reads it before any service start; SYSTEM never reads it.
            } else {
                try{File.Delete(path);File.Delete(path+".reply");Directory.Delete(scratch);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
    }
    static string ReadApproval(string path) {
        try{return File.Exists(path+".reply")?SecureCapturePolicy.ReadBounded(path+".reply",64):"";}catch(IOException){return "";}
    }
}
