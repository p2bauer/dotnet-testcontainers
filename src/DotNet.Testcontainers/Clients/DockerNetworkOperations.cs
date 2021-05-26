namespace DotNet.Testcontainers.Clients
{
  using System;
  using System.Threading;
  using System.Threading.Tasks;
  using Docker.DotNet.Models;

  internal sealed class DockerNetworkOperations : DockerApiClient, IDockerNetworkOperations
  {
    public DockerNetworkOperations(Uri endpoint) : base(endpoint)
    {
    }

    public async Task<string> CreateNetworkAsync(string name, CancellationToken ct = default)
    {
      try
      {
        var response = await this.Docker.Networks.CreateNetworkAsync(new NetworksCreateParameters
        {
          Name = name
        }, ct).ConfigureAwait(false);

        if (response == null)
          throw new Exception("CreateNetworkAsync returned null");

        return response.ID;
      }
      catch (Exception ex)
      {
        throw;
      }
    }
  }
}
