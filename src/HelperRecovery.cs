using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

sealed class HelperRecovery
{
    readonly Func<uint> currentChild;
    readonly Action<uint> start;
    readonly Func<long> time;
    readonly object gate = new object();
    uint lastChild;
    long lastAttempt;
    bool attempted;
    public HelperRecovery(Func<uint> currentChild, Action<uint> start, Func<long> time) { this.currentChild = currentChild; this.start = start; this.time = time; }
    public bool TryStart(uint target)
    {
        lock (gate) {
            if (target == 0 || target > int.MaxValue || currentChild() != target) return false;
            long now = time();
            if (attempted && lastChild == target && now - lastAttempt < 15000) return false;
            if (currentChild() != target) return false;
            attempted = true; lastChild = target; lastAttempt = now;
            start(target);
            return true;
        }
    }
}

static class NativeAgentRecovery
{
    public static void Start(string root, uint target)
    {
        string exe = Path.Combine(Path.GetFullPath(root), "ChildStream.exe");
        if (!File.Exists(exe) || !HostController.SamePath(exe, System.Windows.Forms.Application.ExecutablePath)) throw new IOException("Open ChildStream from its installed folder before recovering the helper.");
        if (AgentClient.CurrentChild() != target) throw new IOException("The child desktop changed before helper recovery.");
        string sid; using (var identity = WindowsIdentity.GetCurrent()) sid = identity.User.Value;
        string name = "ChildStream.Recover." + Guid.NewGuid().ToString("N");
        var objects = new List<object>(); object folderObject = null; bool registered = false;
        try {
            object serviceObject = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")); objects.Add(serviceObject); dynamic service = serviceObject; service.Connect();
            folderObject = service.GetFolder(@"\"); objects.Add(folderObject); dynamic folder = folderObject;
            object definitionObject = service.NewTask(0); objects.Add(definitionObject); dynamic definition = definitionObject;
            object principalObject = definition.Principal; objects.Add(principalObject); dynamic principal = principalObject;
            principal.UserId = sid; principal.LogonType = 3; principal.RunLevel = 0; // Interactive token, least privilege.
            object settingsObject = definition.Settings; objects.Add(settingsObject); dynamic settings = settingsObject;
            settings.Enabled = true; settings.Hidden = true; settings.AllowDemandStart = true;
            settings.DisallowStartIfOnBatteries = false; settings.StopIfGoingOnBatteries = false;
            settings.ExecutionTimeLimit = "PT0S"; settings.MultipleInstances = 0;
            object actionsObject = definition.Actions; objects.Add(actionsObject); dynamic actions = actionsObject;
            object actionObject = actions.Create(0); objects.Add(actionObject); dynamic action = actionObject;
            action.Path = exe; action.Arguments = NativeArguments.Join(new[] { "-agent", target.ToString(CultureInfo.InvariantCulture) }); action.WorkingDirectory = root;
            object taskObject = folder.RegisterTaskDefinition(name, definition, 2, sid, null, 3, null); objects.Add(taskObject); registered = true; dynamic task = taskObject;
            if (AgentClient.CurrentChild() != target) throw new IOException("The child desktop changed before helper launch.");
            object runningObject = task.RunEx(null, 12, checked((int)target), sid); objects.Add(runningObject); // Bind session ID and the current user's SID.
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 3000) {
                if (AgentClient.CurrentChild() != target) throw new IOException("The child desktop changed during helper recovery.");
                if (HostNative.Processes(exe).Any(p => p.SessionId == (int)target && HostController.SamePath(p.ExecutablePath, exe))) return;
                Thread.Sleep(75);
            }
            throw new IOException("Windows did not start the helper in the child desktop. Check Task Scheduler and autostart.log.");
        }
        finally {
            if (registered) {
                try { dynamic folder = folderObject; folder.DeleteTask(name, 0); }
                catch (Exception ex) { Program.Log("temporary helper task cleanup failed for " + name + ": " + ex.Message); }
            }
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null && Marshal.IsComObject(objects[i])) Marshal.FinalReleaseComObject(objects[i]);
        }
    }
}