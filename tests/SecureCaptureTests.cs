using System;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;

static class SecureCaptureTests
{
    static Type Policy { get { var t=typeof(Program).Assembly.GetType("SecureCapturePolicy"); if(t==null)throw new Exception("SecureCapturePolicy is missing");return t; } }
    static object Call(string method,params object[] args) {try{return Policy.GetMethod(method).Invoke(null,args);}catch(TargetInvocationException e){throw e.InnerException;}}
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    static void Rejected(Action action){try{action();}catch(ArgumentException){return;}catch(InvalidDataException){return;}throw new Exception("Unsafe input accepted");}
    public static void Run(Action<string,Action> check)
    {
        check("secure capture store identity uses installation user and host path",delegate {
            string a=(string)Call("StoreKey",@"C:\Portable","S-1-5-21-1",@"D:\Host\sunshine.exe");
            Assert(a==(string)Call("StoreKey",@"c:\portable\","S-1-5-21-1",@"d:\host\SUNSHINE.EXE"),"path spelling changed identity");
            Assert(a!=(string)Call("StoreKey",@"C:\Other","S-1-5-21-1",@"D:\Host\sunshine.exe"),"installation not isolated");
            Assert(a!=(string)Call("StoreKey",@"C:\Portable","S-1-5-21-2",@"D:\Host\sunshine.exe"),"user not isolated");
            Assert(a!=(string)Call("StoreKey",@"C:\Portable","S-1-5-21-1",@"E:\Host\sunshine.exe"),"host not isolated");
        });
        check("secure payload refuses traversal absolute paths ADS and executable assets",delegate {
            foreach(string item in new[]{@"..\escape.dll",@"C:\escape.dll",@"assets\..\escape.dll",@"assets\x:stream",@"assets\run.exe",@"scripts\run.ps1"})
                Rejected(()=>Call("PayloadPath",@"C:\Snapshot",item));
            Assert((string)Call("PayloadPath",@"C:\Snapshot",@"assets\web\index.html")==@"C:\Snapshot\assets\web\index.html","asset lost");
            Assert((string)Call("PayloadPath",@"C:\Snapshot","sunshine.exe")==@"C:\Snapshot\sunshine.exe","host lost");
            Assert((string)Call("PayloadPath",@"C:\Snapshot","avcodec.dll")==@"C:\Snapshot\avcodec.dll","DLL lost");
        });
        check("secure capture configuration is bounded Desktop capture without launch commands",delegate {
            string config=(string)Call("Configuration",@"C:\Protected\data","Fixture PC",48989);
            Assert(config.Contains("capture = ddx")&&!config.Contains("capture = wgc"),"wrong capture");
            foreach(string key in new[]{"dd_configuration_option","install_steam_audio_drivers","nvenc_opengl_vulkan_on_dxgi","upnp"})
                Assert(config.Contains(key+" = disabled"),"unsafe global option "+key);
            Assert(config.Contains("origin_web_ui_allowed = pc"),"web UI exposed remotely");
            foreach(string key in new[]{"file_state","credentials_file","pkey","cert","file_apps","log_path"})
                Assert(config.Contains(key+" = C:\\Protected\\data"),"state not protected");
            Assert(!config.Contains("prep")&&!config.Contains("cmd ="),"command copied");
            Rejected(()=>Call("Configuration",@"C:\bad#path","PC",48989));
            Rejected(()=>Call("Configuration",@"C:\Protected","PC\r\nglobal_prep_cmd = bad",48989));
            Rejected(()=>Call("Configuration",@"C:\Protected","PC",1028));
        });
        check("secure capture requires initialized bounded web login",delegate {
            Assert(!(bool)Call("CredentialsValid","{}"),"unclaimed SYSTEM web UI allowed");
            Assert(!(bool)Call("CredentialsValid","{\"username\":\"u\",\"password\":\"\",\"salt\":\"s\"}"),"empty password accepted");
            Assert(!(bool)Call("CredentialsValid","not json"),"bad JSON accepted");
            Assert(!(bool)Call("CredentialsValid",new string('x',65537)),"oversize login accepted");
            Assert((bool)Call("CredentialsValid","{\"username\":\"u\",\"password\":\""+new string('A',64)+"\",\"salt\":\"abcdefghijklmnop\"}"),"valid existing login rejected");
        });
        check("secure process attestation rejects stale and future heartbeats",delegate {
            long now=DateTime.UtcNow.Ticks;
            Assert((bool)Call("FreshStatus",now,now),"current status rejected");
            Assert((bool)Call("FreshStatus",now-TimeSpan.FromSeconds(4).Ticks,now),"fresh status rejected");
            Assert(!(bool)Call("FreshStatus",now-TimeSpan.FromSeconds(6).Ticks,now),"stale trusted PID accepted");
            Assert(!(bool)Call("FreshStatus",now+TimeSpan.FromSeconds(3).Ticks,now),"future status accepted");
            Assert(!(bool)Call("FreshStatus",0L,now),"missing heartbeat accepted");
        });
        check("secure store ACL refuses any ordinary user write grant",delegate {
            var acl=new DirectorySecurity();var admin=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null);
            var system=new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null);
            var user=WindowsIdentity.GetCurrent().User;
            acl.SetAccessRuleProtection(true,false);acl.SetOwner(admin);
            acl.AddAccessRule(new FileSystemAccessRule(admin,FileSystemRights.FullControl,AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(system,FileSystemRights.FullControl,AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(user,FileSystemRights.ReadAndExecute,AccessControlType.Allow));
            Assert((bool)Call("OnlyPrivilegedWriters",acl),"protected ACL refused");
            acl.AddAccessRule(new FileSystemAccessRule(user,FileSystemRights.WriteData,AccessControlType.Allow));
            Assert(!(bool)Call("OnlyPrivilegedWriters",acl),"user-writable payload trusted");
        });
        check("secure capture uses fixed identity migration files only",delegate {
            var field=Policy.GetField("IdentityFiles");
            var files=(string[])field.GetValue(null);
            Assert(string.Join("|",files)=="credentials.json|sunshine_state.json|private-key.pem|certificate.pem","unexpected configuration/apps/scripts imported");
        });
    }
}
