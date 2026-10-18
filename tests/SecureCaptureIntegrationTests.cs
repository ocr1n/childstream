using System;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Linq;
using System.Reflection;

static class SecureCaptureIntegrationTests
{
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    sealed class Source:IHostDiscoverySource {
        readonly string exe;readonly bool virtualFile;internal Source(string exe,bool virtualFile=false){this.exe=exe;this.virtualFile=virtualFile;}
        public IEnumerable<HostCandidate> Candidates(string root){return new[]{new HostCandidate(exe,HostKind.Sunshine,"registry")};}
        public bool Exists(string path){return virtualFile||File.Exists(path);}
        public HostFileMetadata Inspect(string path){return new HostFileMetadata("Sunshine","Sunshine","2026.1017.22144","fixture");}
    }
    public static void Run(Action<string,Action> check)
    {
        check("internal capture snapshots cannot be selected as installed hosts",delegate {
            string parent=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ChildStream Capture");
            string exe=Path.Combine(parent,"profile","runs","run","host","sunshine.exe");
            var source=new Source(exe,true);
            Assert(HostDiscovery.Find(Path.GetTempPath(),source).Installations.Length==0,"internal snapshot offered as another installation");
            bool refused=false;try{HostDiscovery.FromExecutable(exe,source);}catch(InvalidDataException){refused=true;}
            Assert(refused,"browsing allowed an internal snapshot");
            string external=Path.Combine(parent+" External","sunshine.exe");
            Assert(HostDiscovery.FromExecutable(external,new Source(external,true))!=null,"a separate custom installation was excluded");
        });
        check("host start binds the authenticated viewer PID rather than enumerating relays",delegate {
            var constructor=typeof(AgentService).GetConstructors().FirstOrDefault(c=>c.GetParameters().Length==9);
            Assert(constructor!=null,"contextual host launcher is missing");
            var handle=typeof(AgentService).GetMethod("Handle",new[]{typeof(AgentRequest),typeof(int)});
            Assert(handle!=null,"authenticated caller PID is missing");
            string root=Path.Combine(Path.GetTempPath(),"capture-caller-"+Guid.NewGuid().ToString("N"));
            try {
                string exe=Path.Combine(root,"host","sunshine.exe");Directory.CreateDirectory(Path.GetDirectoryName(exe));File.WriteAllText(exe,"fixture");
                int seen=0;using(var completed=new ManualResetEvent(false)) {
                    var service=(AgentService)constructor.Invoke(new object[]{root,7,new Source(exe),new Func<string,HostProcess[]>(p=>new HostProcess[0]),
                        new Func<ChildHostProfile,HostLaunchReceipt>(p=>{throw new Exception("legacy callback used");}),new Func<bool>(()=>true),null,null,
                        new Func<ChildHostProfile,int,HostLaunchReceipt>((p,pid)=>{seen=pid;completed.Set();return new HostLaunchReceipt(123,1,p.Identity);})});
                    var reply=(AgentReply)handle.Invoke(service,new object[]{new AgentRequest{Command="STATUS"},111});
                    handle.Invoke(service,new object[]{new AgentRequest{Command="START_HOST",SelectionId=reply.SelectionId},888});
                    Assert(completed.WaitOne(3000)&&seen==888,"wrong viewer process supervised the capture");
                }
            } finally {if(Directory.Exists(root))Directory.Delete(root,true);}
        });
        check("protected host evidence precedes access-denied legacy enumeration",delegate {
            string root=Path.Combine(Path.GetTempPath(),"capture-controller-"+Guid.NewGuid().ToString("N"));
            try {
                string exe=Path.Combine(root,"host","sunshine.exe");Directory.CreateDirectory(Path.GetDirectoryName(exe));File.WriteAllText(exe,"fixture");
                var host=new HostInstallation(exe,HostKind.Sunshine,new HostFileMetadata("Sunshine","Sunshine","2026.1017.22144","fixture"),"browse");
                var p=HostProfiles.Prepare(root,host,new NetworkPort[0]);
                var ctor=typeof(HostController).GetConstructors().FirstOrDefault(c=>c.GetParameters().Length==5);
                Assert(ctor!=null,"protected-status hook is missing");
                var evidence=new HostSnapshot(HostState.Running,"protected fixture",p.BasePort);
                var controller=(HostController)ctor.Invoke(new object[]{p,7,new Func<HostProcess[]>(()=>{throw new Exception("legacy query must not run");}),
                    new Func<HostLaunchReceipt>(()=>{throw new Exception("duplicate launch");}),new Func<HostSnapshot>(()=>evidence)});
                Assert(controller.GetStatus().State==HostState.Running,"SYSTEM host appeared stopped");
                evidence=new HostSnapshot(HostState.Unknown,"stale trusted status",0);
                Assert(controller.RequestStart().State==HostState.Unknown&&!controller.StartInProgress,"stale evidence launched a duplicate");
            } finally {if(Directory.Exists(root))Directory.Delete(root,true);}
        });
    }
}
