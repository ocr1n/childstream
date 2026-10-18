using System;
using System.Linq;

sealed class HostInstallPolicy
{
    AgentHost[] known = new AgentHost[0];
    bool missing;
    public bool HasHosts { get { return known.Length > 0; } }
    public bool Prominent { get { return missing && !HasHosts; } }
    public string Locations { get { return string.Join(Environment.NewLine, known.Select(item => item.Name + ": " + item.Path)); } }
    public string Confirmation { get { return HasHosts ? "You already have " + string.Join(", ", known.Select(item => item.Name).Distinct()) + " installed. Do you want to install another streaming host?" : null; } }
    public void Update(AgentReply reply) {
        if (reply.Hosts.Length > 0) { known = reply.Hosts; missing = false; }
        else if (reply.Status.State == HostState.Missing) { known = new AgentHost[0]; missing = true; }
        // A missing helper is not evidence that installed hosts have disappeared.
    }
    public void UpdateState(HostState state) { if (state == HostState.Missing) { known = new AgentHost[0]; missing = true; } }
}
