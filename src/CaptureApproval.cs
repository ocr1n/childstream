using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

// Only this normal-user relay lives on the physical desktop. Windows consent is
// always a human action; the resulting supervisor survives child-session logoff.
static class CaptureApproval
{
    internal static int Relay(string[] args)
    {
        if(args.Length!=3)return 2;
        string response=args[1]+".reply";bool normalToken=false;
        try {
            using(var identity=WindowsIdentity.GetCurrent()) {
                if(identity.User.IsWellKnown(WellKnownSidType.LocalSystemSid)||new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))return 1;
            }
            normalToken=true;
            var plan=SecureCapturePlan.Read(args[1],args[2]);
            SecureCapturePolicy.Require(HostNative.CurrentSession()==SecureCapturePolicy.SupervisorSession(plan.ParentSession,plan.Child)&&SecureCaptureLauncher.Sid==plan.Sid,"Capture approval must run as the bound normal parent user.");
            SecureCapturePolicy.Require(SecureCapturePolicy.Same(SecureCaptureLauncher.Self,plan.InstallExe)&&SecureCapturePolicy.Hash(SecureCaptureLauncher.Self)==plan.SelfHash,"Capture approval installation changed.");
            new SecureCaptureRuntime(plan).VerifyBinding();
            File.WriteAllText(response,"waiting");
            var start=new ProcessStartInfo(SecureCaptureLauncher.Self,NativeArguments.Join(new[]{"-capture-setup",args[1],args[2]})){
                UseShellExecute=true,Verb="runas",WorkingDirectory=plan.InstallRoot,WindowStyle=ProcessWindowStyle.Normal
            };
            using(var setup=Process.Start(start)) {
                if(setup==null)throw new IOException("Windows returned no capture supervisor.");
                File.WriteAllText(response,"approved");
                setup.WaitForExit();
                try{File.WriteAllText(response,"exit="+setup.ExitCode);}catch(IOException){}catch(UnauthorizedAccessException){}
                return setup.ExitCode;
            }
        } catch(Win32Exception ex) {
            try{if(normalToken)File.WriteAllText(response,ex.NativeErrorCode==1223?"canceled":"failed");}catch(IOException){}catch(UnauthorizedAccessException){}
            return 1;
        } catch(Exception) {
            // Only a verified normal token may write this user-owned reply.
            // Do not include launch-plan or credential content.
            try{if(normalToken)File.WriteAllText(response,"failed");}catch(IOException){}catch(UnauthorizedAccessException){}
            return 1;
        }
    }
    internal static void Start(SecureCapturePlan plan,string path,string digest)
    {
        SecureCapturePolicy.Require(HostNative.CurrentSession()==plan.Child&&SecureCaptureLauncher.Sid==plan.Sid,"Only the bound child helper may request capture.");
        SecureCapturePolicy.Require(HostNative.WTSGetActiveConsoleSessionId()==(uint)plan.ParentSession,"The physical desktop changed.");
        string name="ChildStream.CaptureApproval."+plan.RunId;
        var objects=new List<object>();object folderObject=null;bool registered=false;
        try {
            object serviceObject=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));objects.Add(serviceObject);dynamic service=serviceObject;service.Connect();
            folderObject=service.GetFolder(@"\");objects.Add(folderObject);dynamic folder=folderObject;
            object definitionObject=service.NewTask(0);objects.Add(definitionObject);dynamic definition=definitionObject;
            object principalObject=definition.Principal;objects.Add(principalObject);dynamic principal=principalObject;
            principal.UserId=plan.Sid;principal.LogonType=3;principal.RunLevel=0;
            object settingsObject=definition.Settings;objects.Add(settingsObject);dynamic settings=settingsObject;
            settings.Enabled=true;settings.Hidden=true;settings.AllowDemandStart=true;settings.ExecutionTimeLimit="PT0S";
            settings.DisallowStartIfOnBatteries=false;settings.StopIfGoingOnBatteries=false;settings.MultipleInstances=0;
            object actionsObject=definition.Actions;objects.Add(actionsObject);dynamic actions=actionsObject;
            object actionObject=actions.Create(0);objects.Add(actionObject);dynamic action=actionObject;
            action.Path=plan.InstallExe;action.Arguments=NativeArguments.Join(new[]{"-capture-request",path,digest});action.WorkingDirectory=plan.InstallRoot;
            object taskObject=folder.RegisterTaskDefinition(name,definition,2,plan.Sid,null,3,null);objects.Add(taskObject);registered=true;dynamic task=taskObject;
            object runningObject=task.RunEx(null,12,plan.ParentSession,plan.Sid);objects.Add(runningObject);
            var clock=Stopwatch.StartNew();
            while(clock.ElapsedMilliseconds<5000) {
                if(File.Exists(path+".reply"))return;
                Thread.Sleep(50);
            }
            throw new IOException("The normal parent approval relay did not acknowledge startup. Check Task Scheduler policy.");
        } finally {
            if(registered)try{dynamic folder=folderObject;folder.DeleteTask(name,0);}catch(Exception ex){Program.Log("temporary capture task cleanup failed: "+ex.GetType().Name);}
            for(int i=objects.Count-1;i>=0;i--)if(objects[i]!=null&&Marshal.IsComObject(objects[i]))Marshal.FinalReleaseComObject(objects[i]);
        }
    }
}
