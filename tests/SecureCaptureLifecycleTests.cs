using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

static class SecureCaptureLifecycleTests
{
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Reject(Action action){try{action();}catch(ArgumentException){return;}catch(InvalidDataException){return;}catch(TargetInvocationException ex){if(ex.InnerException is InvalidDataException||ex.InnerException is ArgumentException)return;throw;}throw new Exception("Unsafe data accepted");}
    sealed class Fixture:IDisposable
    {
        public readonly string Root=Path.Combine(Path.GetTempPath(),"capture-fixture-"+Guid.NewGuid().ToString("N"));
        public Fixture(){Directory.CreateDirectory(Root);}
        public string DirectoryAt(string name){string p=Path.Combine(Root,name);Directory.CreateDirectory(p);return p;}
        public string Identity(string name) {
            string p=DirectoryAt(name);
            File.WriteAllText(Path.Combine(p,"credentials.json"),"{\"username\":\"fixture\",\"password\":\""+new string('A',64)+"\",\"salt\":\"fixture-salt-1234\"}");
            File.WriteAllText(Path.Combine(p,"sunshine_state.json"),"{\"root\":{\"uniqueid\":\"b5e621d9-bdaa-40c3-a8f5-f2d4539a93f2\",\"named_devices\":[]}}");
            File.WriteAllText(Path.Combine(p,"private-key.pem"),"-----BEGIN PRIVATE KEY-----\nZml4dHVyZS1vcGFxdWUtYnl0ZXM=\n-----END PRIVATE KEY-----");
            File.WriteAllText(Path.Combine(p,"certificate.pem"),"-----BEGIN CERTIFICATE-----\nZml4dHVyZS1vcGFxdWUtYnl0ZXM=\n-----END CERTIFICATE-----");
            return p;
        }
        public void Dispose(){Directory.Delete(Root,true);}
    }
    public static void Run(Action<string,Action> check)
    {
        check("capture startup rejects cancellation and expired approval before execution",delegate {
            var method=typeof(SecureCapturePolicy).GetMethod("EnsureStartupAllowed");Assert(method!=null,"startup cancellation gate is missing");
            long now=SecureCapturePolicy.Timestamp;
            method.Invoke(null,new object[]{now+TimeSpan.FromSeconds(1).Ticks,now,false});
            Reject(()=>method.Invoke(null,new object[]{now+TimeSpan.FromSeconds(1).Ticks,now,true}));
            Reject(()=>method.Invoke(null,new object[]{now-1,now,false}));
        });
        check("capture supervisor outlives child logoff by using the parent session",delegate {
            var method=typeof(SecureCapturePolicy).GetMethod("SupervisorSession");Assert(method!=null,"SupervisorSession is missing");
            Assert((int)method.Invoke(null,new object[]{1,8})==1,"supervisor would be killed with the child session");
            Reject(()=>method.Invoke(null,new object[]{8,8}));Reject(()=>method.Invoke(null,new object[]{0,8}));
        });
        check("privileged entry points reject missing consent system identity and malformed actions",delegate {
            Assert(SecureCaptureRuntime.Execute(new[]{"-capture-worker",@"C:\invalid"})==1,"normal user became SYSTEM worker");
            Assert(SecureCaptureRuntime.Execute(new[]{"-capture-setup",@"C:\missing-plan",new string('0',64)})==1,"invalid setup accepted");
            Assert(SecureCaptureRuntime.Execute(new[]{"-capture-setup"})==2,"malformed CLI accepted");
        });
        check("status snapshots remain readable across atomic replacement",delegate {
            var open=typeof(SecureCapturePolicy).GetMethod("OpenSnapshot",BindingFlags.Static|BindingFlags.NonPublic);
            Assert(open!=null,"OpenSnapshot is missing");
            var type=typeof(Program).Assembly.GetType("CaptureFileSecurity");
            var boundary=(CaptureFileSecurity)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{new Action<string,bool>((p,d)=>{}),new Action<string,bool>((p,d)=>{})},null);
            using(var f=new Fixture()) {
                string path=Path.Combine(f.Root,"status");File.WriteAllText(path,"before");
                using(var held=(FileStream)open.Invoke(null,new object[]{path,100})) {
                    SecureCapturePolicy.AtomicText(path,"after",boundary);
                    using(var reader=new StreamReader(held))Assert(reader.ReadToEnd()=="before","opened snapshot changed beneath reader");
                    Assert(SecureCapturePolicy.ReadBounded(path,100)=="after","new snapshot was not published");
                }
                Reject(()=>open.Invoke(null,new object[]{path,1}));
            }
        });
        check("capture plan rejects changes to entire JSON across approval",delegate {
            using(var f=new Fixture()) {
                string path=Path.Combine(f.Root,"request.json");
                File.WriteAllText(path,"{}");string hash=SecureCapturePolicy.Hash(path);
                File.WriteAllText(path,"{\"Child\":123}");
                Reject(()=>SecureCapturePlan.Read(path,hash));
                File.WriteAllText(path,new string('x',2*1024*1024+1));Reject(()=>SecureCapturePlan.Read(path,SecureCapturePolicy.Hash(path)));
            }
        });
        check("identity validation rejects missing UUID and truncated state without rewriting input",delegate {
            using(var f=new Fixture()) {
                string p=f.Identity("source");SecureCaptureStore.ValidateIdentity(p);
                string state=Path.Combine(p,"sunshine_state.json");
                File.WriteAllText(state,"{\"root\":{}}");Reject(()=>SecureCaptureStore.ValidateIdentity(p));
                File.WriteAllText(state,"{\"root\":");Reject(()=>SecureCaptureStore.ValidateIdentity(p));
                Assert(File.ReadAllText(state)=="{\"root\":","bad state was repaired by inventing a new identity");
            }
        });
        check("protected status evaluation treats stopped prior child as stopped and stale live evidence as unknown",delegate {
            var method=typeof(SecureCapturePolicy).GetMethod("EvidenceStatus");Assert(method!=null,"EvidenceStatus is missing");
            string root=@"C:\fixture-profile",run=new string('a',32),source=@"D:\host\sunshine.exe",profile=new string('b',64);
            long now=SecureCapturePolicy.Timestamp;
            var e=new SecureCaptureEvidence{RunId=run,SourceExecutable=source,ActualExecutable=Path.Combine(root,"runs",run,"host","sunshine.exe"),
                ProfileIdentity=profile,Child=7,ParentSession=1,Phase="Stopped",BasePort=48989};
            var lookup=new Func<int,int>(pid=>pid==20?0:pid==30?1:8);
            Func<HostSnapshot> evaluate=()=> (HostSnapshot)method.Invoke(null,new object[]{e,root,source,profile,8,now,lookup});
            Assert(evaluate()==null,"old stopped child blocked a new child");
            e.Phase="RecoveryRequired";
            Assert(evaluate().State==HostState.Unknown&&evaluate().Message.ToLowerInvariant().Contains("pairing"),"failed pairing save was hidden as ordinary stopped");

            e.Child=8;e.Phase="Running";e.HostPid=10;e.HostTicks=1;e.WorkerPid=20;e.SetupPid=30;e.Heartbeat=now-TimeSpan.FromSeconds(10).Ticks;
            Assert(evaluate().State==HostState.Unknown,"stale trusted PID allowed a duplicate startup");
            e.Heartbeat=now;Assert(evaluate().State==HostState.Running,"fresh owned host not recognized");
            e.ProfileIdentity=new string('c',64);Assert(evaluate().State==HostState.Unknown,"changed configuration accepted");
            e.ProfileIdentity=profile;e.ActualExecutable=source;Reject(()=>evaluate());
        });
        check("capture profile lease excludes concurrent privileged starts",delegate {
            var method=typeof(SecureCaptureStore).GetMethod("AcquireLease",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(string),typeof(CaptureFileSecurity)},null);
            Assert(method!=null,"AcquireLease is missing");
            var security=new CaptureFileSecurity((p,d)=>{},(p,d)=>{});
            using(var f=new Fixture()) {
                using(var first=(FileStream)method.Invoke(null,new object[]{f.Root,security})) {
                    bool refused=false;try{using((FileStream)method.Invoke(null,new object[]{f.Root,security})){} }catch(TargetInvocationException ex){refused=ex.InnerException is IOException;}
                    Assert(refused,"concurrent capture acquired the same pairing store");
                }
                using((FileStream)method.Invoke(null,new object[]{f.Root,security})){}
            }
        });
        check("pairing generations commit atomically preserve bytes and refuse broken updates",delegate {
            var securityType=typeof(Program).Assembly.GetType("CaptureFileSecurity");Assert(securityType!=null,"CaptureFileSecurity is missing");
            object boundary=Activator.CreateInstance(securityType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{new Action<string,bool>((p,d)=>{}),new Action<string,bool>((p,d)=>{})},null);
            var commit=typeof(SecureCaptureStore).GetMethod("Commit",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(string),typeof(string),typeof(string),securityType},null);
            Assert(commit!=null,"transactional commit boundary is missing");
            using(var f=new Fixture()) {
                string root=f.DirectoryAt("profile"),source=f.Identity("original");
                string first=(string)commit.Invoke(null,new[]{(object)root,source,null,boundary});
                foreach(string name in SecureCapturePolicy.IdentityFiles)Assert(File.ReadAllBytes(Path.Combine(first,name)).SequenceEqual(File.ReadAllBytes(Path.Combine(source,name))),"migration changed identity bytes");
                string pointer=File.ReadAllText(Path.Combine(root,"saved-generation"));
                string work=f.Identity("work");
                File.WriteAllText(Path.Combine(work,"sunshine_state.json"),"{\"root\":{\"uniqueid\":\"b5e621d9-bdaa-40c3-a8f5-f2d4539a93f2\",\"named_devices\":[{\"name\":\"Fixture Client\"}]}}");
                string second=(string)commit.Invoke(null,new object[]{root,work,first,boundary});
                Assert(second!=first&&File.ReadAllText(Path.Combine(second,"sunshine_state.json")).Contains("Fixture Client"),"pairing update lost");
                Assert(File.ReadAllText(Path.Combine(first,"sunshine_state.json")).Contains("\"named_devices\":[]"),"last-good backup overwritten");
                string secondPointer=File.ReadAllText(Path.Combine(root,"saved-generation"));
                File.WriteAllText(Path.Combine(work,"sunshine_state.json"),"{broken");
                Reject(()=>commit.Invoke(null,new object[]{root,work,second,boundary}));
                Assert(File.ReadAllText(Path.Combine(root,"saved-generation"))==secondPointer&&pointer!=secondPointer,"failed commit replaced the active generation");
                File.WriteAllText(Path.Combine(work,"sunshine_state.json"),File.ReadAllText(Path.Combine(second,"sunshine_state.json")));
                File.WriteAllText(Path.Combine(work,"sunshine_state.json"),File.ReadAllText(Path.Combine(second,"sunshine_state.json")).Replace("b5e621d9-bdaa-40c3-a8f5-f2d4539a93f2","bcf698bc-a14c-4d60-9f04-a0354f7243fb"));
                Reject(()=>commit.Invoke(null,new object[]{root,work,second,boundary}));
                File.WriteAllText(Path.Combine(work,"sunshine_state.json"),File.ReadAllText(Path.Combine(second,"sunshine_state.json")));
                File.AppendAllText(Path.Combine(work,"private-key.pem"),"changed");
                Reject(()=>commit.Invoke(null,new object[]{root,work,second,boundary}));
            }
        });
    }
}
