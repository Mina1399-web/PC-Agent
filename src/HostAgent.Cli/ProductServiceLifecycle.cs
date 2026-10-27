using System.ServiceProcess;
namespace HostAgent.Cli;

public interface IProductServiceHandle:IDisposable
{
    ServiceControllerStatus Status {get;}
    IReadOnlyList<string> RunningDependents {get;}
    void Start();
    void Stop(bool includeDependents);
    Task WaitAsync(ServiceControllerStatus expected,TimeSpan timeout,CancellationToken token);
}
public sealed class ProductServiceLifecycle(Func<string,IProductServiceHandle> open)
{
    private static readonly string[] ProductNames=["HostAgent","HostAgent.Helper"];
    public static ProductServiceLifecycle Native()=>new(name=>new NativeHandle(name));
    public async Task RunAsync(bool start,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!start)
            foreach(var name in ProductNames)
            {
                using var service=open(name);
                if(service.RunningDependents.Any(x=>!ProductNames.Contains(x,StringComparer.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("unrelated_dependent_service");
            }
        var failures=new List<Exception>();
        foreach(var name in start?ProductNames.Reverse():ProductNames)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var service=open(name);
                var desired=start?ServiceControllerStatus.Running:ServiceControllerStatus.Stopped;
                if(service.Status==desired)continue;
                if(start)service.Start();else service.Stop(false);
                await service.WaitAsync(desired,TimeSpan.FromSeconds(30),token);
            }
            catch(Exception error) when(start&&!token.IsCancellationRequested)
            {
                // Monitoring can recover even if the independent helper cannot start.
                // Preserve every failure and report it after trying both fixed services.
                failures.Add(error);
            }
        }
        if(failures.Count>0)throw new AggregateException("product_service_recovery_failed",failures);
    }
    private sealed class NativeHandle(string name):IProductServiceHandle
    {
        private readonly ServiceController _service=new(name);
        public ServiceControllerStatus Status {get{_service.Refresh();return _service.Status;}}
        public IReadOnlyList<string> RunningDependents
        {
            get
            {
                var names=new List<string>();
                foreach(var dependent in _service.DependentServices)
                    using(dependent){if(dependent.Status!=ServiceControllerStatus.Stopped)names.Add(dependent.ServiceName);}
                return names;
            }
        }
        public void Start()=>_service.Start();
        public void Stop(bool includeDependents)=>_service.Stop(includeDependents);
        public async Task WaitAsync(ServiceControllerStatus expected,TimeSpan timeout,CancellationToken token)
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            while(Status!=expected)
            {
                if(watch.Elapsed>=timeout)throw new System.TimeoutException("product_service_timeout");
                await Task.Delay(100,token);
            }
        }
        public void Dispose()=>_service.Dispose();
    }
}
