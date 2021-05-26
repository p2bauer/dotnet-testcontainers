namespace DotNet.Testcontainers.Clients
{
  using System.Threading;
  using System.Threading.Tasks;

  internal interface IDockerNetworkOperations
  {
    Task<string> CreateNetworkAsync(string name, CancellationToken ct = default);
  }
}
