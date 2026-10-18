using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

sealed class CaptureFileSecurity
{
    internal readonly Action<string,bool> Trusted, Own;
    internal CaptureFileSecurity(Action<string,bool> trusted,Action<string,bool> own){Trusted=trusted;Own=own;}
    internal static readonly CaptureFileSecurity Native=new CaptureFileSecurity(SecureCapturePolicy.Trusted,SecureCapturePolicy.Own);
}

static class SecureCapturePolicy
{
    public static readonly string[] IdentityFiles = { "credentials.json", "sunshine_state.json", "private-key.pem", "certificate.pem" };
    internal static readonly SecurityIdentifier Admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null);
    internal static readonly SecurityIdentifier SystemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null);
    internal static void Require(bool ok,string message) { if(!ok)throw new InvalidDataException(message); }
    internal static bool Same(string a,string b) { return HostController.SamePath(a,b); }
    public static string StoreKey(string root,string sid,string executable) {
        return HostProfiles.Digest(Path.GetFullPath(root).TrimEnd('\\','/').ToUpperInvariant()+"|"+sid+"|"+Path.GetFullPath(executable).ToUpperInvariant());
    }
    internal static string ParentRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ChildStream Capture"); } }
    internal static bool IsInternalSnapshot(string path) {
        return Path.GetFullPath(path).StartsWith(Path.GetFullPath(ParentRoot).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
    }
    internal static string ProfileRoot(string root,string sid,string exe) { return Path.Combine(ParentRoot,StoreKey(root,sid,exe)); }
    internal static string RunRoot(string profile,string run) { Guid id;Require(Guid.TryParseExact(run,"N",out id),"Invalid capture run identity."); return Path.Combine(profile,"runs",run); }
    public static string PayloadPath(string root,string relative) {
        Require(!string.IsNullOrWhiteSpace(relative)&&relative.Length<=1024&&!Path.IsPathRooted(relative)&&relative.IndexOf(':')<0,"Invalid capture payload path.");
        string[] parts=relative.Replace('/','\\').Split('\\');
        Require(parts.All(p=>p.Length>0&&p!="."&&p!=".."),"Capture payload traversal refused.");
        string extension=Path.GetExtension(relative);
        bool rootFile=parts.Length==1&&(relative.Equals("sunshine.exe",StringComparison.OrdinalIgnoreCase)||extension.Equals(".dll",StringComparison.OrdinalIgnoreCase));
        bool asset=parts.Length>1&&parts[0].Equals("assets",StringComparison.OrdinalIgnoreCase)&&
            !new[]{".exe",".dll",".com",".bat",".cmd",".ps1",".vbs",".jscript",".msi",".scr"}.Contains(extension.ToLowerInvariant());
        Require(rootFile||asset,"Only the host, root DLLs and non-executable assets may enter a capture snapshot.");
        string path=Path.GetFullPath(Path.Combine(root,relative));
        Require(path.StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase),"Capture payload escaped its root.");
        return path;
    }
    public static string Configuration(string data,string machine,int port) {
        HostPorts.Family(port);
        Require(data.IndexOfAny(new[]{'#','\r','\n','"'})<0&&machine.IndexOfAny(new[]{'\r','\n','#'})<0,"Capture settings contain unrepresentable characters.");
        var text=new StringBuilder("# ChildStream protected child capture.\r\n");
        text.AppendLine("capture = ddx");text.AppendLine("dd_configuration_option = disabled");
        text.AppendLine("install_steam_audio_drivers = disabled");text.AppendLine("nvenc_opengl_vulkan_on_dxgi = disabled");
        text.AppendLine("upnp = disabled");text.AppendLine("origin_web_ui_allowed = pc");
        text.AppendLine("sunshine_name = "+machine+" Child Desktop");text.AppendLine("port = "+port);
        string[] keys={"file_state","credentials_file","pkey","cert","file_apps","log_path"};
        string[] files={"sunshine_state.json","credentials.json","private-key.pem","certificate.pem","apps.json","sunshine.log"};
        for(int i=0;i<keys.Length;i++)text.AppendLine(keys[i]+" = "+Path.Combine(data,files[i]));
        return text.ToString();
    }
    internal static IDictionary<string,object> Json(string text,int max) {
        Require(text!=null&&text.Length<=max,"Capture JSON is too large.");
        try {
            var value=new JavaScriptSerializer{MaxJsonLength=max,RecursionLimit=32}.DeserializeObject(text) as IDictionary<string,object>;
            Require(value!=null,"Capture JSON must be an object.");return value;
        } catch(ArgumentException){throw new InvalidDataException("Capture JSON is malformed.");}
          catch(InvalidOperationException){throw new InvalidDataException("Capture JSON is malformed.");}
    }
    public static bool CredentialsValid(string text) {
        try { var d=Json(text,65536);object user,password,salt;
            return d.TryGetValue("username",out user)&&user is string&&!string.IsNullOrWhiteSpace((string)user)&&
                d.TryGetValue("password",out password)&&password is string&&((string)password).Length==64&&((string)password).All(Uri.IsHexDigit)&&
                d.TryGetValue("salt",out salt)&&salt is string&&!string.IsNullOrEmpty((string)salt);
        } catch(Exception e){if(e is ArgumentException||e is InvalidOperationException||e is InvalidDataException)return false;throw;}
    }
    public static void EnsureStartupAllowed(long deadline,long now,bool canceled){Require(!canceled,"Capture startup was canceled.");Require(deadline>0&&now<=deadline,"Capture approval expired before startup.");}
    public static int SupervisorSession(int parent,int child){Require(parent>0&&child>0&&parent!=child,"Capture supervision must be outside the child session.");return parent;}
    public static bool FreshStatus(long timestamp,long now) { return timestamp>0&&timestamp<=now+TimeSpan.FromSeconds(2).Ticks&&timestamp>=now-TimeSpan.FromSeconds(5).Ticks; }
    public static HostSnapshot EvidenceStatus(SecureCaptureEvidence e,string root,string source,string profile,int session,long now,Func<int,int> processSession) {
        Require(e!=null&&Same(e.SourceExecutable,source),"Protected host source changed.");
        Require(Same(e.ActualExecutable,Path.Combine(RunRoot(root,e.RunId),"host","sunshine.exe")),"Protected host provenance changed.");
        if(e.Phase=="Stopped")return null;
        if(e.Phase=="RecoveryRequired")return new HostSnapshot(HostState.Unknown,"The host stopped, but new pairing or password changes could not be saved. Last-good identity is intact. Recover the working identity before restarting: "+RunRoot(root,e.RunId),0);
        if(e.Child!=session||e.ProfileIdentity!=profile)return new HostSnapshot(HostState.Unknown,"The protected host belongs to an earlier child session or configuration. No duplicate will be started.",0);
        if(!FreshStatus(e.Heartbeat,now))return new HostSnapshot(HostState.Unknown,"The protected host heartbeat expired. No duplicate will be started. Check the protected capture diagnostics.",0);
        HostPorts.Family(e.BasePort);
        if(e.Phase=="Starting")return new HostSnapshot(HostState.Starting,"Starting protected child capture. Handle its initial Windows approval on the host PC if asked.",e.BasePort);
        Require(e.Phase=="Running"&&e.HostPid>0&&e.HostTicks>0&&e.WorkerPid>0&&e.SetupPid>0,"Protected host evidence is incomplete.");
        Require(processSession(e.HostPid)==session&&processSession(e.WorkerPid)==0&&processSession(e.SetupPid)==SupervisorSession(e.ParentSession,session),"Protected capture process sessions changed.");
        return new HostSnapshot(HostState.Running,"Sunshine uses protected DXGI capture in this child desktop. UAC stays enabled; accept or cancel prompts yourself through your client.",e.BasePort);
    }
    internal static long Timestamp { get { return (long)(Stopwatch.GetTimestamp()*(10000000.0/Stopwatch.Frequency)); } }
    public static bool OnlyPrivilegedWriters(FileSystemSecurity acl) {
        var owner=acl.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if(owner==null||(!owner.Equals(Admin)&&!owner.Equals(SystemSid)))return false;
        const FileSystemRights writes=FileSystemRights.WriteData|FileSystemRights.AppendData|FileSystemRights.WriteAttributes|FileSystemRights.WriteExtendedAttributes|FileSystemRights.Delete|FileSystemRights.DeleteSubdirectoriesAndFiles|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership;
        foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier)))
            if(rule.AccessControlType==AccessControlType.Allow&&(rule.FileSystemRights&writes)!=0&&!rule.IdentityReference.Equals(Admin)&&!rule.IdentityReference.Equals(SystemSid))return false;
        return true;
    }
    internal static void NoLinks(string path) {
        string p=Path.GetFullPath(path);
        while(p!=null){Require((File.GetAttributes(p)&FileAttributes.ReparsePoint)==0,"Capture refuses reparse points.");p=Path.GetDirectoryName(p);}
    }
    internal static void Trusted(string path,bool directory) {
        NoLinks(path);
        FileSystemSecurity acl=directory?(FileSystemSecurity)Directory.GetAccessControl(path):File.GetAccessControl(path);
        Require(OnlyPrivilegedWriters(acl),"Capture store is writable by a non-privileged identity.");
    }
    internal static DirectorySecurity Acl(string sid,bool parent) {
        var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);acl.SetOwner(Admin);
        foreach(var id in new[]{Admin,SystemSid})acl.AddAccessRule(new FileSystemAccessRule(id,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(parent?new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null):new SecurityIdentifier(sid),FileSystemRights.ReadAndExecute,parent?InheritanceFlags.None:InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        return acl;
    }
    internal static void EnsureDirectory(string path,string sid,bool parent) {
        if(!Directory.Exists(path))Directory.CreateDirectory(path,Acl(sid,parent));
        Trusted(path,true);
    }
    internal static void Own(string path,bool directory) {
        SecurityIdentifier owner;using(var identity=WindowsIdentity.GetCurrent())owner=identity.User.Equals(SystemSid)?SystemSid:Admin;
        if(directory){var a=Directory.GetAccessControl(path);a.SetOwner(owner);Directory.SetAccessControl(path,a);}
        else{var a=File.GetAccessControl(path);a.SetOwner(owner);File.SetAccessControl(path,a);}
    }
    internal static string Hash(string path) { using(var f=File.OpenRead(path))using(var h=SHA256.Create())return Hex(h.ComputeHash(f)); }
    internal static string Hex(byte[] bytes){return BitConverter.ToString(bytes).Replace("-","");}
    internal static FileStream OpenSnapshot(string path,int limit) {
        NoLinks(path);
        var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        try{Require(stream.Length<=limit,"Capture file is too large.");return stream;}catch{stream.Dispose();throw;}
    }
    internal static string ReadBounded(string path,int limit) {
        using(var stream=OpenSnapshot(path,limit))using(var reader=new StreamReader(stream,Encoding.UTF8,true)) {
            var text=new StringBuilder();var buffer=new char[4096];int count;
            while((count=reader.Read(buffer,0,buffer.Length))>0){Require(text.Length+count<=limit,"Capture file grew beyond its size limit.");text.Append(buffer,0,count);}
            return text.ToString();
        }
    }
    internal static void AtomicText(string path,string text) {AtomicText(path,text,CaptureFileSecurity.Native);}
    internal static void AtomicText(string path,string text,CaptureFileSecurity security) {
        string temp=Path.Combine(Path.GetDirectoryName(path),".write-"+Guid.NewGuid().ToString("N"));
        try {using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] bytes=new UTF8Encoding(false).GetBytes(text);stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
            security.Own(temp,false);
            if(File.Exists(path)){security.Trusted(path,false);File.Replace(temp,path,null);}else File.Move(temp,path);
        } finally {if(File.Exists(temp))File.Delete(temp);}
    }
}

static class SecureCaptureStore
{
    internal static FileStream AcquireLease(string profile) {return AcquireLease(profile,CaptureFileSecurity.Native);}
    internal static FileStream AcquireLease(string profile,CaptureFileSecurity security) {
        string path=Path.Combine(profile,"run.lock");
        if(File.Exists(path))security.Trusted(path,false);
        var lease=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);
        try{security.Own(path,false);return lease;}catch{lease.Dispose();throw;}
    }
    static string StateUuid(string directory) {
        var json=SecureCapturePolicy.Json(SecureCapturePolicy.ReadBounded(Path.Combine(directory,"sunshine_state.json"),8*1024*1024),8*1024*1024);
        return (string)((IDictionary<string,object>)json["root"])["uniqueid"];
    }
    internal static string SavedDirectory(string profile) {
        SecureCapturePolicy.Trusted(profile,true);
        string pointer=Path.Combine(profile,"saved-generation");
        if(!File.Exists(pointer))return null;
        SecureCapturePolicy.Trusted(pointer,false);
        string id=SecureCapturePolicy.ReadBounded(pointer,64).Trim();Guid guid;
        SecureCapturePolicy.Require(Guid.TryParseExact(id,"N",out guid),"Invalid saved capture identity.");
        string root=Path.Combine(profile,"saved");SecureCapturePolicy.Trusted(root,true);
        string path=Path.Combine(root,id);SecureCapturePolicy.Trusted(path,true);ValidateIdentity(path);return path;
    }
    internal static void ValidateIdentity(string directory) {
        foreach(string name in SecureCapturePolicy.IdentityFiles){string path=Path.Combine(directory,name);SecureCapturePolicy.NoLinks(path);SecureCapturePolicy.Require(File.Exists(path)&&new FileInfo(path).Length>0&&new FileInfo(path).Length<=8*1024*1024,"Incomplete child pairing identity.");}
        SecureCapturePolicy.Require(SecureCapturePolicy.CredentialsValid(SecureCapturePolicy.ReadBounded(Path.Combine(directory,"credentials.json"),65536)),"Child web login is uninitialized or malformed.");
        var state=SecureCapturePolicy.Json(SecureCapturePolicy.ReadBounded(Path.Combine(directory,"sunshine_state.json"),8*1024*1024),8*1024*1024);
        object root;SecureCapturePolicy.Require(state.TryGetValue("root",out root)&&root is IDictionary<string,object>,"Child pairing state is malformed.");
        object uuid;Guid parsed;
        var document=(IDictionary<string,object>)root;
        SecureCapturePolicy.Require(document.TryGetValue("uniqueid",out uuid)&&uuid is string&&Guid.TryParse((string)uuid,out parsed)&&parsed!=Guid.Empty,"Child pairing UUID is missing or invalid.");
        string cert=SecureCapturePolicy.ReadBounded(Path.Combine(directory,"certificate.pem"),65536);
        string key=SecureCapturePolicy.ReadBounded(Path.Combine(directory,"private-key.pem"),65536);
        SecureCapturePolicy.Require(cert.Contains("-----BEGIN CERTIFICATE-----")&&cert.Contains("-----END CERTIFICATE-----")&&key.Contains("PRIVATE KEY-----")&&key.Contains("-----END "),"Child certificate/key is incomplete.");
    }
    internal static void CopyIdentity(string source,string destination) {CopyIdentity(source,destination,CaptureFileSecurity.Native);}
    internal static void CopyIdentity(string source,string destination,CaptureFileSecurity security) {
        Directory.CreateDirectory(destination);
        foreach(string name in SecureCapturePolicy.IdentityFiles) {
            string from=Path.Combine(source,name),to=Path.Combine(destination,name);
            SecureCapturePolicy.NoLinks(from);
            using(var input=new FileStream(from,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var output=new FileStream(to,FileMode.CreateNew,FileAccess.Write,FileShare.None)){SecureCapturePolicy.Require(input.Length<=8*1024*1024,"Child identity is too large.");input.CopyTo(output);output.Flush(true);}
            SecureCapturePolicy.Require(SecureCapturePolicy.Hash(from)==SecureCapturePolicy.Hash(to),"Child identity changed during snapshot.");security.Own(to,false);
        }
        security.Own(destination,true);ValidateIdentity(destination);
    }
    internal static string Commit(string profile,string source,string previous) {return Commit(profile,source,previous,CaptureFileSecurity.Native);}
    internal static string Commit(string profile,string source,string previous,CaptureFileSecurity security) {
        ValidateIdentity(source);
        if(previous!=null)SecureCapturePolicy.Require(string.Equals(StateUuid(source),StateUuid(previous),StringComparison.OrdinalIgnoreCase),"Host UUID changed unexpectedly; last-good pairing was preserved.");
        if(previous!=null)foreach(string name in new[]{"private-key.pem","certificate.pem"})
            SecureCapturePolicy.Require(SecureCapturePolicy.Hash(Path.Combine(source,name))==SecureCapturePolicy.Hash(Path.Combine(previous,name)),"Host changed its certificate identity unexpectedly; last-good pairing was preserved.");
        string saved=Path.Combine(profile,"saved");Directory.CreateDirectory(saved);security.Own(saved,true);
        string id=Guid.NewGuid().ToString("N"),draft=Path.Combine(saved,".draft-"+id),target=Path.Combine(saved,id);
        CopyIdentity(source,draft,security);
        SecureCapturePolicy.NoLinks(saved);SecureCapturePolicy.NoLinks(draft);
        SecureCapturePolicy.Require(Path.GetDirectoryName(Path.GetFullPath(target))==Path.GetFullPath(saved),"Invalid identity commit target.");
        Directory.Move(draft,target);
        SecureCapturePolicy.AtomicText(Path.Combine(profile,"saved-generation"),id,security);
        return target; // Previous generations are intentionally retained as recovery backups.
    }
}
