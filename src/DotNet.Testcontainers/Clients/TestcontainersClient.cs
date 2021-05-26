namespace DotNet.Testcontainers.Clients
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text;
  using System.Threading;
  using System.Threading.Tasks;
  using Docker.DotNet;
  using Docker.DotNet.Models;
  using DotNet.Testcontainers.Containers.Configurations;
  using DotNet.Testcontainers.Containers.OutputConsumers;
  using DotNet.Testcontainers.Images.Configurations;
  using DotNet.Testcontainers.Services;
  using ICSharpCode.SharpZipLib.Tar;

  public sealed class TestcontainersClient : ITestcontainersClient
  {
    public const string TestcontainersLabel = "dotnet.testcontainers";

    public const string TestcontainersCleanUpLabel = TestcontainersLabel + ".cleanUp";

    private readonly string osRootDirectory = Path.GetPathRoot(Directory.GetCurrentDirectory());

    private readonly TestcontainersRegistryService registryService;

    private readonly IDockerContainerOperations containers;

    private readonly IDockerImageOperations images;

    private readonly IDockerSystemOperations system;

    private readonly IDockerNetworkOperations network;

    public TestcontainersClient() : this(
      DockerApiEndpoint.Local)
    {
    }

    public TestcontainersClient(Uri endpoint) : this(
      new TestcontainersRegistryService(),
      new DockerContainerOperations(endpoint),
      new DockerImageOperations(endpoint),
      new DockerSystemOperations(endpoint),
      new DockerNetworkOperations(endpoint))
    {
    }

    private TestcontainersClient(
      TestcontainersRegistryService registryService,
      IDockerContainerOperations containerOperations,
      IDockerImageOperations imageOperations,
      IDockerSystemOperations systemOperations,
      IDockerNetworkOperations networkOperations)
    {
      this.registryService = registryService;
      this.containers = containerOperations;
      this.images = imageOperations;
      this.system = systemOperations;
      this.network = networkOperations;
    }

    ~TestcontainersClient()
    {
      this.Dispose();
    }

    public bool IsRunningInsideDocker
    {
      get
      {
        return File.Exists(Path.Combine(this.osRootDirectory, ".dockerenv"));
      }
    }

    public void Dispose()
    {
      GC.SuppressFinalize(this);
    }

    public Task<bool> GetIsWindowsEngineEnabled(CancellationToken ct = default)
    {
      return this.system.GetIsWindowsEngineEnabled(ct);
    }

    public Task<ContainerListResponse> GetContainer(string id, CancellationToken ct = default)
    {
      return this.containers.ByIdAsync(id, ct);
    }

    public Task<long> GetContainerExitCode(string id, CancellationToken ct = default)
    {
      return this.containers.GetExitCode(id, ct);
    }

    public async Task StartAsync(string id, CancellationToken ct = default)
    {
      if (await this.containers.ExistsWithIdAsync(id, ct)
        .ConfigureAwait(false))
      {
        await this.containers.StartAsync(id, ct)
          .ConfigureAwait(false);
      }
    }

    public async Task StopAsync(string id, CancellationToken ct = default)
    {
      if (await this.containers.ExistsWithIdAsync(id, ct)
        .ConfigureAwait(false))
      {
        await this.containers.StopAsync(id, ct)
          .ConfigureAwait(false);
      }
    }

    public async Task RemoveAsync(string id, CancellationToken ct = default)
    {
      if (await this.containers.ExistsWithIdAsync(id, ct)
        .ConfigureAwait(false))
      {
        try
        {
          await this.containers.RemoveAsync(id, ct)
            .ConfigureAwait(false);
        }
        catch (DockerApiException e)
        {
          // The Docker daemon may already start the progress to removes the container (AutoRemove).
          // https://docs.docker.com/engine/api/v1.41/#operation/ContainerCreate.
          if (!e.Message.Contains($"removal of container {id} is already in progress"))
          {
            throw;
          }
        }
      }

      this.registryService.Unregister(id);
    }

    public Task AttachAsync(string id, IOutputConsumer outputConsumer, CancellationToken ct = default)
    {
      return this.containers.AttachAsync(id, outputConsumer, ct);
    }

    public Task<long> ExecAsync(string id, IList<string> command, CancellationToken ct = default)
    {
      return this.containers.ExecAsync(id, command, ct);
    }

    public async Task CopyFileAsync(string id, string filePath, byte[] fileContent, int accessMode, int userId, int groupId, CancellationToken ct = default)
    {
      using (var memStream = new MemoryStream())
      {
        using (var tarOutputStream = new TarOutputStream(memStream, Encoding.Default))
        {
          tarOutputStream.IsStreamOwner = false;
          tarOutputStream.PutNextEntry(
            new TarEntry(
              new TarHeader
              {
                Name = filePath,
                UserId = userId,
                GroupId = groupId,
                Mode = accessMode,
                Size = fileContent.Length
              }));

          await tarOutputStream.WriteAsync(fileContent, 0, fileContent.Length, ct)
            .ConfigureAwait(false);

          tarOutputStream.CloseEntry();
        }

        memStream.Seek(0, SeekOrigin.Begin);

        await this.containers.ExtractArchiveToContainerAsync(id, "/", memStream, ct)
          .ConfigureAwait(false);
      }
    }

    public async Task<string> RunAsync(ITestcontainersConfiguration configuration, CancellationToken ct = default)
    {
      // Killing or canceling the test process will prevent the cleanup.
      // Remove labeled, orphaned containers from previous runs.
      var removeOrphanedContainersTasks = (await this.containers.GetOrphanedObjects(ct)
          .ConfigureAwait(false))
        .Select(container => this.containers.RemoveAsync(container.ID, ct));

      if (!await this.images.ExistsWithNameAsync(configuration.Image.FullName, ct)
        .ConfigureAwait(false))
      {
        await this.images.CreateAsync(configuration.Image, configuration.AuthConfig, ct)
          .ConfigureAwait(false);
      }

      await Task.WhenAll(removeOrphanedContainersTasks)
        .ConfigureAwait(false);

      var id = await this.containers.RunAsync(configuration, ct)
        .ConfigureAwait(false);

      this.registryService.Register(id, configuration.CleanUp);

      return id;
    }

    public Task<string> BuildAsync(IImageFromDockerfileConfiguration configuration, CancellationToken ct = default)
    {
      return this.images.BuildAsync(configuration, ct);
    }

    public Task<string> CreateNetworkAsync(string name, CancellationToken ct = default)
    {
      return this.network.CreateNetworkAsync(name, ct);
    }

    public Task DeleteNetworkAsync(string networkId, CancellationToken ct = default)
    {
      return this.network.DeleteNetworkAsync(networkId, ct);
    }
  }
}
