using HostAgent.Core.Models.Policy;
namespace HostAgent.Core.Contracts;
public interface IPolicyEngine { PolicyDecision Evaluate(PolicyContext? context); }
